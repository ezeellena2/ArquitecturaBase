using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// How the shared outgoing chooses the next chat (H12 and H31), with PostgreSQL, Redis and Meta simulated: what answers someone goes
/// before the rules' notices, a person in their six-second wait does not hold back the others, a person with a send in progress is
/// not taken again, and within a chat the order stays.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppRoleNoticeQueueTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // H31: with the notices of a role already in the queue, an operator's reply to another chat goes out first; the notices follow
    // in their order.
    [Fact]
    public async Task A_manual_reply_goes_out_before_the_notices_of_a_role_already_in_the_queue()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct, clock);
        var runId = Guid.NewGuid();
        const int roleRecipients = 3;
        for (var index = 0; index < roleRecipients; index++)
        {
            var recipient = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
            await NoticeAsync(scenario, runId, recipient.UserId, "Aviso de rol " + index);
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }
        var manualRecipient = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        var actor = await scenario.OperatorAsync([WhatsAppPermissions.Read, WhatsAppPermissions.Send], Ct);
        using var client = scenario.Host.CreateClient();
        using var accepted = await client.SendAsync(HttpMethod.Post, $"/api/whatsapp/chats/{manualRecipient.ContactId}/messages",
            JsonContent.Create(new { requestId = Guid.NewGuid(), body = "Atención manual" }), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var worker = scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        var dispatchMilliseconds = new List<double>();
        for (var index = 0; index <= roleRecipients; index++)
        {
            var elapsed = Stopwatch.StartNew();
            await worker.ProcessNextAsync(Ct);
            dispatchMilliseconds.Add(elapsed.Elapsed.TotalMilliseconds);
            Assert.Equal(index + 1, scenario.Cloud.Messages.Count);
        }

        Assert.Equal(["Atención manual", "Aviso de rol 0", "Aviso de rol 1", "Aviso de rol 2"], Bodies(scenario));
        await worker.ProcessNextAsync(Ct);
        Assert.Equal(roleRecipients + 1, scenario.Cloud.Messages.Count);
        await WhatsAppRoleQueueMeasurement.WriteAsync(roleRecipients, 1, dispatchMilliseconds, Ct);
    }

    // H31 with the order of a chat: Ana has a notice at the head and a reply behind it, so her chat goes first and her notice goes
    // out before Beto's older one; her reply follows in her order, after her six seconds, when nobody else is left.
    [Fact]
    public async Task A_chat_with_a_reply_behind_its_notice_goes_first_and_keeps_its_order()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct, clock);
        var ana = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        var beto = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        await NoticeAsync(scenario, Guid.NewGuid(), beto.UserId, "Aviso para Beto");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await NoticeAsync(scenario, Guid.NewGuid(), ana.UserId, "Aviso para Ana");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var actor = await scenario.OperatorAsync([WhatsAppPermissions.Read, WhatsAppPermissions.Send], Ct);
        using var client = scenario.Host.CreateClient();
        using var accepted = await client.SendAsync(HttpMethod.Post, $"/api/whatsapp/chats/{ana.ContactId}/messages",
            JsonContent.Create(new { requestId = Guid.NewGuid(), body = "Respuesta para Ana" }), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var worker = scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();

        for (var index = 0; index < 3; index++) await worker.ProcessNextAsync(Ct);

        Assert.Equal(["Aviso para Ana", "Aviso para Beto", "Respuesta para Ana"], Bodies(scenario));
    }

    // H12: while Ana waits her six seconds, Beto's message goes out; Ana's second one follows when nobody else is left, in her order.
    [Fact]
    public async Task Two_messages_to_one_person_do_not_hold_back_the_others()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct, clock);
        var ana = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        var beto = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        await NoticeAsync(scenario, Guid.NewGuid(), ana.UserId, "Primero para Ana");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await NoticeAsync(scenario, Guid.NewGuid(), ana.UserId, "Segundo para Ana");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await NoticeAsync(scenario, Guid.NewGuid(), beto.UserId, "Para Beto");
        var worker = scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();

        for (var index = 0; index < 3; index++) await worker.ProcessNextAsync(Ct);

        Assert.Equal(["Primero para Ana", "Para Beto", "Segundo para Ana"], Bodies(scenario));
    }

    // The condition H23 asks for, seen with one copy: while Ana's send is in progress nothing else of hers is taken, so another copy
    // cannot overtake it; Beto's goes on, and once nothing else can go out the queue has nothing to give.
    [Fact]
    public async Task A_person_with_a_send_in_progress_is_not_taken_while_the_others_go_out()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct, clock);
        var ana = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        var beto = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        await NoticeAsync(scenario, Guid.NewGuid(), ana.UserId, "Primero para Ana");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await NoticeAsync(scenario, Guid.NewGuid(), ana.UserId, "Segundo para Ana");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await NoticeAsync(scenario, Guid.NewGuid(), beto.UserId, "Para Beto");

        var first = await ReserveAsync(scenario);
        var second = await ReserveAsync(scenario);
        var third = await ReserveAsync(scenario);

        Assert.Equal(("Primero para Ana", ana.ContactId), (first?.Body, first?.ContactId));
        Assert.Equal(("Para Beto", beto.ContactId), (second?.Body, second?.ContactId));
        Assert.Null(third);
    }

    // H7: Ana's first notice does not go out (the connection failed before Meta): it waits for its next attempt in its place, so
    // her second one does not overtake it while Beto's goes out; after the wait, both go out in their order.
    [Fact]
    public async Task A_chat_whose_head_waits_for_its_next_attempt_does_not_go_out_until_then()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct, clock);
        var ana = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        var beto = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        await NoticeAsync(scenario, Guid.NewGuid(), ana.UserId, "Primero para Ana");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await NoticeAsync(scenario, Guid.NewGuid(), ana.UserId, "Segundo para Ana");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await NoticeAsync(scenario, Guid.NewGuid(), beto.UserId, "Para Beto");
        var calls = 0;
        scenario.Cloud.Respond = () => ++calls == 1 ? WhatsAppSendResult.Failed(WhatsAppSendFailure.Transient)
            : WhatsAppSendResult.Sent("wamid.action-" + Guid.NewGuid().ToString("N"));
        var worker = scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();

        await worker.ProcessNextAsync(Ct);
        // Out of her six seconds, Ana's chat still waits for its next attempt: Beto's goes out, and nothing of hers.
        clock.Advance(TimeSpan.FromSeconds(7));
        await worker.ProcessNextAsync(Ct);
        Assert.False(await worker.ProcessNextAsync(Ct));
        Assert.Equal(["Primero para Ana", "Para Beto"], Bodies(scenario));

        clock.Advance(TimeSpan.FromSeconds(24));
        for (var index = 0; index < 2; index++) await worker.ProcessNextAsync(Ct);

        Assert.Equal(["Primero para Ana", "Para Beto", "Primero para Ana", "Segundo para Ana"], Bodies(scenario));
    }

    // H7: a send that keeps not going out is retried twice, each after its wait, and the third refusal fails it like any other.
    [Fact]
    public async Task A_notice_that_keeps_not_going_out_fails_after_its_two_retries()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct, clock);
        var ana = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        var run = Guid.NewGuid();
        await NoticeAsync(scenario, run, ana.UserId, "Para Ana");
        scenario.Cloud.Respond = () => WhatsAppSendResult.Failed(WhatsAppSendFailure.RateLimited, 130429);
        var worker = scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.True(await worker.ProcessNextAsync(Ct));
            clock.Advance(TimeSpan.FromSeconds(31));
        }

        Assert.False(await worker.ProcessNextAsync(Ct));
        Assert.Equal(["Para Ana", "Para Ana", "Para Ana"], Bodies(scenario));
        Assert.Equal(StructuredRecordActionState.Failed, (await scenario.ScopeAsync(services => services.GetServices<IStructuredRecordActionChannel>()
            .Single(channel => channel.ChannelId == "phone").GetAsync(run, ana.UserId, Ct))).State);
    }

    // H3: with Redis down the turn to write to the person cannot be taken; Meta was not called, so the notice goes back to the queue
    // in its place and waits, without failing nor spending one of its retries (three outages are more than its two), and goes out
    // once Redis is back.
    [Fact]
    public async Task A_notice_whose_turn_cannot_be_taken_waits_and_goes_out_once_redis_is_back()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var coordinator = new FailingDispatchCoordinator(failures: 3);
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct, clock, coordinator);
        var ana = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        var run = Guid.NewGuid();
        await NoticeAsync(scenario, run, ana.UserId, "Para Ana");
        var worker = scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();

        for (var outage = 0; outage < 3; outage++)
        {
            Assert.True(await worker.ProcessNextAsync(Ct));
            Assert.False(await worker.ProcessNextAsync(Ct));
            clock.Advance(TimeSpan.FromSeconds(31));
        }
        Assert.Empty(scenario.Cloud.Messages);
        Assert.True(await worker.ProcessNextAsync(Ct));

        Assert.Equal(["Para Ana"], Bodies(scenario));
        Assert.Equal(4, coordinator.Attempts);
        Assert.Equal(0, await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>()
            .Where(outgoing => outgoing.ContactId == ana.ContactId).Select(outgoing => outgoing.DispatchRetries).SingleAsync(Ct)));
        Assert.Equal(StructuredRecordActionState.Accepted, (await scenario.ScopeAsync(services => services.GetServices<IStructuredRecordActionChannel>()
            .Single(channel => channel.ChannelId == "phone").GetAsync(run, ana.UserId, Ct))).State);
    }

    // H3: Redis goes down between taking the turn and releasing it; the notice already went out, and that is what stays.
    [Fact]
    public async Task Releasing_the_turn_without_redis_does_not_change_what_happened_to_the_send()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct, clock, new FailingDispatchCoordinator(failures: 0, failRelease: true));
        var ana = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        var run = Guid.NewGuid();
        await NoticeAsync(scenario, run, ana.UserId, "Para Ana");

        Assert.True(await scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct));

        Assert.Equal(["Para Ana"], Bodies(scenario));
        Assert.Equal(StructuredRecordActionState.Accepted, (await scenario.ScopeAsync(services => services.GetServices<IStructuredRecordActionChannel>()
            .Single(channel => channel.ChannelId == "phone").GetAsync(run, ana.UserId, Ct))).State);
    }

    // H9: the gauges of the outgoing read what waits now: the pending ones, the uncertain ones and the oldest pending.
    [Fact]
    public async Task The_workload_of_the_outgoing_counts_what_waits_now()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct, clock);
        var ana = await scenario.RecipientAsync(clock.GetUtcNow().UtcDateTime, Ct);
        var first = clock.GetUtcNow().UtcDateTime;
        await NoticeAsync(scenario, Guid.NewGuid(), ana.UserId, "Primero para Ana");
        clock.Advance(TimeSpan.FromSeconds(5));
        await NoticeAsync(scenario, Guid.NewGuid(), ana.UserId, "Segundo para Ana");

        var workload = await scenario.ScopeAsync(services => services.GetRequiredService<IWhatsAppOutgoingWorkloadReader>().FindAsync(Ct));

        Assert.Equal((2, 0), (workload.Pending, workload.Uncertain));
        Assert.Equal(first, workload.OldestPendingAtUtc!.Value, TimeSpan.FromMilliseconds(1));
    }

    private static async Task NoticeAsync(WhatsAppFormActionScenario scenario, Guid runId, Guid userId, string text)
    {
        var queued = await scenario.ScopeAsync(services => services.GetServices<IStructuredRecordActionChannel>()
            .Single(channel => channel.ChannelId == "phone").EnqueueAsync(new StructuredRecordActionMessage(runId, userId, text), Ct));
        Assert.Equal(StructuredRecordActionState.Pending, queued.State);
    }

    private static async Task<WhatsAppDispatchResponse?> ReserveAsync(WhatsAppFormActionScenario scenario)
    {
        var reserved = await scenario.ScopeAsync(services => services.GetRequiredService<IWhatsAppChatDispatchService>().ReserveAsync(Ct));
        Assert.True(reserved.IsSuccess);
        return reserved.Value.Operation;
    }

    private static string?[] Bodies(WhatsAppFormActionScenario scenario) =>
        [.. scenario.Cloud.Messages.Select(message => Assert.IsType<WhatsAppTextMessage>(message).Body)];
}
