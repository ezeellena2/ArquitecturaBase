using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Assistant;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantAutomaticIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_offers_share_the_business_day_limit_and_replay_does_not_consume_a_third_offer()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct);
        var presentation = await host.ScopeAsync(services => services.GetRequiredService<ISystemSettingsReader>().FindPresentationAsync(Ct));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(presentation.DefaultTimeZoneId);
        var boundaryLocal = TimeZoneInfo.ConvertTime(host.Clock.GetUtcNow(), zone).Date.AddDays(2);
        var boundaryUtc = TimeZoneInfo.ConvertTimeToUtc(boundaryLocal, zone);
        host.Clock.SetUtcNow(new DateTimeOffset(boundaryUtc.AddMinutes(-3)));
        var contact = await host.ContactAsync(Ct, automatic: true);
        var first = await PrepareAsync(host, contact.ContactId);
        Assert.NotNull(first.Operation);
        Assert.Equal(contact.RecipientId, first.Operation.Context.ActorUserId); Assert.Equal(contact.RecipientId, first.Operation.Context.SubjectUserId);
        var outcome = host.Outcome(contact, first, "ready");
        await host.CompleteOutcomeAsync(contact.ContactId, outcome, Ct);
        await host.CompleteOutcomeAsync(contact.ContactId, outcome, Ct);
        Assert.Equal((1, 1), await CountsAsync(host, contact.ContactId));
        var replay = await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantDispatchService>().PrepareAsync(contact.ContactId, Ct));
        Assert.True(replay.IsSuccess); Assert.Null(replay.Value);
        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);

        contact = contact with { SourceMessageId = await host.AddNewerSourceAsync(contact, Ct) };
        var second = await PrepareAsync(host, contact.ContactId);
        await host.CompleteOutcomeAsync(contact.ContactId, host.Outcome(contact, second, "ready"), Ct);
        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        Assert.Equal((2, 2), await CountsAsync(host, contact.ContactId));

        contact = contact with { SourceMessageId = await host.AddNewerSourceAsync(contact, Ct) };
        var third = await PrepareAsync(host, contact.ContactId);
        await host.CompleteOutcomeAsync(contact.ContactId, host.Outcome(contact, third, "ready"), Ct);
        Assert.Equal((2, 2), await CountsAsync(host, contact.ContactId));
        Assert.Equal(WhatsAppAssistantRequestState.Blocked, await StateAsync(host, third.RequestId));
        Assert.Equal(2, host.Cloud.Messages.Count);

        host.Clock.SetUtcNow(new DateTimeOffset(boundaryUtc.AddMinutes(1)));
        contact = contact with { SourceMessageId = await host.AddNewerSourceAsync(contact, Ct) };
        var nextDay = await PrepareAsync(host, contact.ContactId);
        await host.CompleteOutcomeAsync(contact.ContactId, host.Outcome(contact, nextDay, "ready"), Ct);
        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        Assert.Equal((3, 3), await CountsAsync(host, contact.ContactId)); Assert.Equal(3, host.Cloud.Messages.Count);
        var fromPreviousUtc = TimeZoneInfo.ConvertTimeToUtc(boundaryLocal.AddDays(-1), zone);
        var toNextUtc = TimeZoneInfo.ConvertTimeToUtc(boundaryLocal.AddDays(1), zone);
        var counts = await host.ScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IWhatsAppAssistantReader>();
            return (Previous: await reader.CountOffersAsync(contact.ContactId, fromPreviousUtc, boundaryUtc, Ct),
                Current: await reader.CountOffersAsync(contact.ContactId, boundaryUtc, toNextUtc, Ct));
        });
        Assert.Equal((2, 1), counts);
    }

    [Fact]
    public async Task Taking_manual_attention_before_a_late_result_prevents_an_automatic_outgoing()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct);
        var contact = await host.ContactAsync(Ct, automatic: true); var work = await PrepareAsync(host, contact.ContactId);
        var outcome = host.Outcome(contact, work, "ready");
        await host.InTransactionAsync(async services =>
        {
            var row = await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().SingleAsync(item => item.Id == contact.ContactId, Ct);
            row.ChangeMode(WhatsAppConversationMode.Manual); return true;
        }, Ct);
        await host.CompleteOutcomeAsync(contact.ContactId, outcome, Ct);
        await host.CompleteOutcomeAsync(contact.ContactId, outcome, Ct);
        Assert.Equal((0, 0), await CountsAsync(host, contact.ContactId)); Assert.Empty(host.Cloud.Messages);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked, await StateAsync(host, work.RequestId));
        using var client = host.Client(host.RestrictedOperator);
        using var thread = await client.GetAsync($"/api/whatsapp/chats/{contact.ContactId}/messages", Ct);
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("recipient")]
    [InlineData("form")]
    [InlineData("values")]
    public async Task Withdrawn_authority_or_owner_values_cannot_create_an_automatic_session(string withdrawn)
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct);
        var contact = await host.ContactAsync(Ct, automatic: true); var work = await PrepareAsync(host, contact.ContactId);
        var outcome = host.Outcome(contact, work, "ready");
        if (withdrawn == "permission")
            await host.InTransactionAsync(async services => { await services.GetRequiredService<IUserRepository>().SetRolesAsync(contact.RecipientId, [], Ct); return true; }, Ct);
        if (withdrawn == "recipient")
            await host.InTransactionAsync(async services =>
            { (await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().SingleAsync(item => item.Id == contact.ContactId, Ct)).UnlinkUser(); return true; }, Ct);
        if (withdrawn == "form") host.Forms.IsPublished = false;
        if (withdrawn == "values") host.Forms.ValuesAvailableForSending = false;
        await host.CompleteOutcomeAsync(contact.ContactId, outcome, Ct);
        Assert.Equal((0, 0), await CountsAsync(host, contact.ContactId)); Assert.Empty(host.Cloud.Messages);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked, await StateAsync(host, work.RequestId));
    }

    [Fact]
    public async Task An_unavailable_AI_channel_does_not_enqueue_a_message_or_read_an_unaccepted_operation()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct);
        host.Operations.AllowSubmit = true; host.Operations.AvailabilityState = "notInstalled";
        host.Operations.SubmissionFailure = AssistantOperationErrors.Unavailable;
        var contact = await host.ContactAsync(Ct, automatic: true);
        var processor = host.Host.Services.GetRequiredService<WhatsAppInboundProcessor>();
        await processor.ProcessPendingAsync(Ct); await processor.ProcessPendingAsync(Ct);
        Assert.Single(host.Operations.SubmitRequests); Assert.Empty(host.Operations.ReadContexts);
        Assert.Equal((0, 0), await CountsAsync(host, contact.ContactId)); Assert.Empty(host.Cloud.Messages);
        using var client = host.Client(host.RestrictedOperator);
        using var thread = await client.GetAsync($"/api/whatsapp/chats/{contact.ContactId}/messages", Ct);
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        Assert.Equal("notInstalled", (await thread.ReadJsonAsync()).GetProperty("assistantAvailability").GetProperty("state").GetString());
    }

    private static async Task<WhatsAppAssistantWorkResponse> PrepareAsync(WhatsAppAssistantScenario host, Guid contactId)
    {
        var result = await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantDispatchService>().PrepareAsync(contactId, Ct));
        Assert.True(result.IsSuccess); return Assert.IsType<WhatsAppAssistantWorkResponse>(result.Value);
    }
    private static Task<WhatsAppAssistantRequestState> StateAsync(WhatsAppAssistantScenario host, Guid requestId) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .Where(item => item.RequestId == requestId).Select(item => item.State).SingleAsync(Ct));
    private static Task<(int Outgoing, int Sessions)> CountsAsync(WhatsAppAssistantScenario host, Guid contactId) => host.ScopeAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        return (await db.Set<WhatsAppOutgoingMessage>().CountAsync(item => item.ContactId == contactId, Ct),
            await db.Set<WhatsAppFlowSession>().CountAsync(item => item.ContactId == contactId, Ct));
    });
}
