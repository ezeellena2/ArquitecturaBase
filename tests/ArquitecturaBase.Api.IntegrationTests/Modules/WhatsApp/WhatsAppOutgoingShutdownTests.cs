using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// H8: a deploy or a restart does not cut what the outgoing has in hand, with PostgreSQL, Redis and Meta simulated. With the
/// shutdown requested nothing new is taken, the call at Meta ends within its own deadline and is confirmed, and a reply that was
/// still waiting for its turn to write to that person goes back to the queue without calling Meta.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppOutgoingShutdownTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_manual_reply_at_Meta_when_the_copy_stops_ends_and_is_confirmed()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct, clock);
        var recipient = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        var outgoingId = await ManualReplyAsync(scenario, recipient.ContactId);
        var atMeta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Cloud.Gate = async token => { atMeta.TrySetResult(); await answer.Task.WaitAsync(token); };
        var worker = scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        using var stopping = new CancellationTokenSource();

        var processing = worker.ProcessNextAsync(stopping.Token);
        await atMeta.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        await stopping.CancelAsync();
        answer.SetResult();

        Assert.True(await processing);
        Assert.Equal(WhatsAppOutgoingMessageState.Accepted, await StateAsync(scenario, outgoingId));
        Assert.Single(scenario.Cloud.Messages);
    }

    [Fact]
    public async Task A_manual_reply_waiting_for_its_turn_when_the_copy_stops_goes_back_to_the_queue()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct, clock);
        var recipient = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        var outgoingId = await ManualReplyAsync(scenario, recipient.ContactId);
        // Someone else is writing to this person: the reply waits for its turn.
        await using var turn = await scenario.Services.GetRequiredService<IWhatsAppDispatchCoordinator>()
            .AcquireAsync(await PhoneAsync(scenario, recipient.ContactId), Ct);
        var worker = scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        using var stopping = new CancellationTokenSource();

        var processing = worker.ProcessNextAsync(stopping.Token);
        var waited = Stopwatch.StartNew();
        while (await StateAsync(scenario, outgoingId) != WhatsAppOutgoingMessageState.Sending && waited.Elapsed < TimeSpan.FromSeconds(30))
            await Task.Delay(TimeSpan.FromMilliseconds(50), TimeProvider.System, Ct);
        await stopping.CancelAsync();

        Assert.True(await processing);
        Assert.Equal(WhatsAppOutgoingMessageState.Pending, await StateAsync(scenario, outgoingId));
        Assert.Empty(scenario.Cloud.Messages);
    }

    private static async Task<Guid> ManualReplyAsync(WhatsAppFormActionScenario scenario, Guid contactId)
    {
        var actor = await scenario.OperatorAsync([WhatsAppPermissions.Read, WhatsAppPermissions.Send], Ct);
        using var client = scenario.Host.CreateClient();
        using var accepted = await client.SendAsync(HttpMethod.Post, $"/api/whatsapp/chats/{contactId}/messages",
            JsonContent.Create(new { requestId = Guid.NewGuid(), body = "Atención manual" }), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        return (await accepted.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static Task<WhatsAppOutgoingMessageState> StateAsync(WhatsAppFormActionScenario scenario, Guid outgoingId) =>
        scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(item => item.Id == outgoingId).Select(item => item.State).SingleAsync(Ct));

    private static async Task<PhoneNumber> PhoneAsync(WhatsAppFormActionScenario scenario, Guid contactId)
    {
        var waId = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().AsNoTracking()
            .Where(item => item.Id == contactId).Select(item => item.WaId).SingleAsync(Ct));
        return PhoneNumber.Create("+" + waId).Value;
    }
}
