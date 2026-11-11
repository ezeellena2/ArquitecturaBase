using System.Net;
using System.Net.Http.Headers;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// «¿La emito?» (EM2-A): an action with choices goes out as reply buttons, and a tap from the recipient's chat reaches the
/// neutral owner once, through the signed webhook and the real inbound processor; the text the owner hands back with an applied
/// tap goes back to that chat once. Meta is simulated; the owner is a capturer.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppFormActionReplyTests(ApiFactory factory)
{
    private const string Route = "/webhooks/whatsapp";
    private static readonly StructuredRecordActionChoice[] Choices = [new("issue", "Emitir"), new("skip", "No emitir")];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private DateTime Now => MetaWebhook.TruncatedToSeconds(factory.Clock.GetUtcNow().UtcDateTime);

    [Fact]
    public async Task An_action_with_choices_goes_out_as_reply_buttons_and_a_retry_sends_the_same_buttons()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        var recipient = await scenario.RecipientAsync(Now, Ct);
        var run = Guid.NewGuid();
        Assert.Equal(StructuredRecordActionState.Pending, (await scenario.ScopeAsync(services =>
            Channel(services).EnqueueAsync(new(run, recipient.UserId, "¿La emito?") { Choices = Choices }, Ct))).State);
        scenario.Cloud.Respond = () => scenario.Cloud.Messages.Count == 1
            ? WhatsAppSendResult.Failed(WhatsAppSendFailure.Other) : WhatsAppSendResult.Sent("wamid.buttons-" + Guid.NewGuid().ToString("N"));
        var worker = scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();

        await worker.ProcessNextAsync(Ct);
        Assert.Equal(StructuredRecordActionState.Pending, (await scenario.ScopeAsync(services => Channel(services).RetryAsync(run, recipient.UserId, 2, Ct))).State);
        await worker.ProcessNextAsync(Ct);

        var first = Assert.IsType<WhatsAppReplyButtonsMessage>(scenario.Cloud.Messages[0]);
        var second = Assert.IsType<WhatsAppReplyButtonsMessage>(scenario.Cloud.Messages[1]);
        Assert.Equal("¿La emito?", first.Body);
        Assert.Equal(["Emitir", "No emitir"], first.Buttons.Select(button => button.Title));
        Assert.All(first.Buttons, button => Assert.StartsWith(WhatsAppFormActionChoice.ReplyPrefix, button.Id, StringComparison.Ordinal));
        Assert.Equal(first.Buttons, second.Buttons);
        Assert.Equal(StructuredRecordActionState.Accepted, (await scenario.ScopeAsync(services => Channel(services).GetAsync(run, recipient.UserId, Ct))).State);
    }

    [Fact]
    public async Task A_tap_hands_the_run_the_person_and_the_choice_to_the_owner_once_and_without_a_text_sends_nothing_back()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        var recipient = await scenario.RecipientAsync(Now, Ct);
        var run = Guid.NewGuid();
        var buttons = await SendAsync(scenario, run, recipient.UserId);
        var contact = await ContactAsync(scenario, recipient.ContactId);
        using var client = scenario.Host.CreateClient();

        await PostTapAsync(client, contact, buttons[0].Id, "Emitir");
        await ProcessAsync(scenario);
        await ProcessAsync(scenario);

        Assert.Equal(new[] { (run, recipient.UserId, "issue") }, scenario.Replies.Calls);

        // A later tap on the other button reaches the owner too, which ignores it: the first one decided.
        await PostTapAsync(client, contact, buttons[1].Id, "No emitir");
        await ProcessAsync(scenario);

        Assert.Equal(new[] { (run, recipient.UserId, "issue"), (run, recipient.UserId, "skip") }, scenario.Replies.Calls);
        Assert.Single(scenario.Cloud.Messages);
        Assert.False(await scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct));
        await AssertHandledAsync(scenario, expected: 2);
    }

    [Fact]
    public async Task The_text_the_owner_hands_back_with_an_applied_tap_goes_back_to_the_chat_once_as_a_text()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        scenario.Replies.Texts["skip"] = "No la emitimos. FAC-000012 quedó anulada.";
        var recipient = await scenario.RecipientAsync(Now, Ct);
        var run = Guid.NewGuid();
        var buttons = await SendAsync(scenario, run, recipient.UserId);
        var contact = await ContactAsync(scenario, recipient.ContactId);
        using var client = scenario.Host.CreateClient();
        var worker = scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();

        await PostTapAsync(client, contact, buttons[1].Id, "No emitir");
        await ProcessAsync(scenario);
        // The same button again: the owner ignores the repeat, so nothing more goes back.
        await PostTapAsync(client, contact, buttons[1].Id, "No emitir");
        await ProcessAsync(scenario);

        Assert.True(await worker.ProcessNextAsync(Ct));
        Assert.False(await worker.ProcessNextAsync(Ct));
        Assert.Equal(new[] { (run, recipient.UserId, "skip"), (run, recipient.UserId, "skip") }, scenario.Replies.Calls);
        Assert.Equal(2, scenario.Cloud.Messages.Count);
        var text = Assert.IsType<WhatsAppTextMessage>(scenario.Cloud.Messages[1]);
        Assert.Equal(("No la emitimos. FAC-000012 quedó anulada.", "+" + contact.WaId), (text.Body, text.To.Value));
        await AssertHandledAsync(scenario, expected: 2);
    }

    [Fact]
    public async Task A_tap_from_another_chat_on_an_unknown_row_or_on_a_choice_not_offered_reaches_no_owner()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        var recipient = await scenario.RecipientAsync(Now, Ct);
        var stranger = await scenario.RecipientAsync(Now, Ct);
        var buttons = await SendAsync(scenario, Guid.NewGuid(), recipient.UserId);
        Assert.True(WhatsAppFormActionChoice.TryParseReplyId(buttons[0].Id, out var outgoingId, out _));
        var contact = await ContactAsync(scenario, recipient.ContactId);
        using var client = scenario.Host.CreateClient();

        await PostTapAsync(client, await ContactAsync(scenario, stranger.ContactId), buttons[0].Id, "Emitir");
        await PostTapAsync(client, contact, WhatsAppFormActionChoice.ReplyId(Guid.CreateVersion7(), "issue"), "Emitir");
        await PostTapAsync(client, contact, WhatsAppFormActionChoice.ReplyId(outgoingId, "other"), "Otra cosa");
        await ProcessAsync(scenario);

        Assert.Empty(scenario.Replies.Calls);
        Assert.Single(scenario.Cloud.Messages);
        await AssertHandledAsync(scenario, expected: 3);
    }

    // A tap its owners did not take stays unhandled and schedules its contact again, read from the unhandled taps of the window:
    // one older than the window, a tap that is no form action's or a contact the round already tried are not asked for.
    [Fact]
    public async Task An_unhandled_tap_schedules_its_contact_only_within_the_window_and_the_next_round_hands_it_over()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        var recipient = await scenario.RecipientAsync(Now, Ct);
        var late = await scenario.RecipientAsync(Now, Ct);
        var run = Guid.NewGuid();
        var buttons = await SendAsync(scenario, run, recipient.UserId);
        await TapAsync(scenario, recipient.ContactId, buttons[0].Id, Now);
        await TapAsync(scenario, late.ContactId, buttons[1].Id, Now - WhatsAppFormActionChoice.PendingReplyWindow - TimeSpan.FromSeconds(1));
        await TapAsync(scenario, late.ContactId, "FORM_AUTHORING:not-an-action", Now);

        Assert.Equal([recipient.ContactId], await PendingContactsAsync(scenario, [], Now));
        Assert.Empty(await PendingContactsAsync(scenario, [recipient.ContactId], Now));
        Assert.Empty(await PendingContactsAsync(scenario, [], Now + WhatsAppFormActionChoice.PendingReplyWindow));

        await ProcessAsync(scenario);

        Assert.Equal(new[] { (run, recipient.UserId, "issue") }, scenario.Replies.Calls);
        Assert.Empty(await PendingContactsAsync(scenario, [], Now));
    }

    private static Task<IReadOnlyList<Guid>> PendingContactsAsync(WhatsAppFormActionScenario scenario, IReadOnlyCollection<Guid> excluded, DateTime nowUtc) =>
        scenario.ScopeAsync(services => services.GetRequiredService<IWhatsAppFormActionReader>().ListContactsWithPendingRepliesAsync(excluded, 50, nowUtc, Ct));

    // A tap already classified and processed by the inbound round, as one whose owners failed is left.
    private static Task<Guid> TapAsync(WhatsAppFormActionScenario scenario, Guid contactId, string replyId, DateTime occurredAtUtc) =>
        scenario.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contactId, Ct);
            var tap = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.ButtonReply, "Emitir", replyId, occurredAtUtc);
            tap.Classify(WhatsAppMessagePurpose.Business); tap.SetInboundSequence(contact.RegisterInboundMessage()); tap.MarkProcessed(occurredAtUtc);
            db.Add(tap);
            return tap.Id;
        }, Ct);

    private static async Task<IReadOnlyList<WhatsAppReplyButton>> SendAsync(WhatsAppFormActionScenario scenario, Guid run, Guid recipientUserId)
    {
        await scenario.ScopeAsync(services => Channel(services).EnqueueAsync(new(run, recipientUserId, "¿La emito?") { Choices = Choices }, Ct));
        await scenario.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        return Assert.IsType<WhatsAppReplyButtonsMessage>(Assert.Single(scenario.Cloud.Messages)).Buttons;
    }

    private static Task<WhatsAppContact> ContactAsync(WhatsAppFormActionScenario scenario, Guid contactId) =>
        scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().AsNoTracking()
            .SingleAsync(contact => contact.Id == contactId, Ct));

    private static Task ProcessAsync(WhatsAppFormActionScenario scenario) =>
        scenario.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);

    // Every tap is stored as a business message and marked handled, so no contact stays scheduled for it.
    private async Task AssertHandledAsync(WhatsAppFormActionScenario scenario, int expected)
    {
        await scenario.ScopeAsync(async services =>
        {
            var taps = await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
                .Where(message => message.Kind == WhatsAppMessageKind.ButtonReply).ToArrayAsync(Ct);
            Assert.Equal(expected, taps.Length);
            Assert.All(taps, tap =>
            {
                Assert.Equal(WhatsAppMessagePurpose.Business, tap.Purpose);
                Assert.NotNull(tap.AssistantProcessedAtUtc);
            });
            Assert.Empty(await services.GetRequiredService<IWhatsAppFormActionReader>().ListContactsWithPendingRepliesAsync([], 50, Now, Ct));
        });
    }

    private async Task PostTapAsync(HttpClient client, WhatsAppContact contact, string id, string title)
    {
        var body = MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(contact.WaId, contact.UserIdentifier, contact.ProfileName)],
            messages: [MetaWebhook.ButtonReply(MetaWebhook.UniqueWaMessageId(), contact.WaId, contact.UserIdentifier, Now, id, title)]);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Route, UriKind.Relative)) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static IStructuredRecordActionChannel Channel(IServiceProvider services) =>
        services.GetServices<IStructuredRecordActionChannel>().Single(channel => channel.ChannelId == "phone");
}
