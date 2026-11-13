using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// W3 (task 23), sent by the assistant: a location the person shares goes to the assistant's open send while the conversation stays
// with the assistant, and its resend continues the assistant's seal, carries the prefill and what was shared, and is no other offer. A
// conversation taken before the location arrives gives the send nothing; taken after, it keeps the resend from going out. A send whose
// sender would not pass the reservation, whose form did not go out, that has a reply or whose values cannot be read takes nothing.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppSharedContentIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_assistant_resend_carries_the_prefill_and_the_location_and_the_offers_do_not_change()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, sharedQuestions: true);
        var contact = await SentAsync(host);
        await AddLocationAsync(host, contact, "Bv. Oroño 1234, S2000 Rosario");

        Assert.Equal(1, (await ProcessAsync(host, contact.ContactId)).Value);
        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);

        var resend = Assert.IsType<WhatsAppFlowMessageResponse>(host.Cloud.Messages[1]);
        Assert.Equal("Puse tu ubicación en «Dirección de entrega». Revisá el formulario «Registrar visita» y confirmalo.", resend.Body);
        var values = resend.InitialValues.Values.SelectMany(screen => screen).ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal(("Sucursal Norte", "Bv. Oroño 1234, S2000 Rosario"),
            (values["field_" + host.Forms.FieldId.ToString("N")], values["field_" + host.Forms.AddressFieldId.ToString("N")]));
        var outgoing = await OutgoingAsync(host, contact.ContactId);
        Assert.Equal([(WhatsAppOutgoingMessageKind.Flow, false, WhatsAppOutgoingMessageState.Accepted), (WhatsAppOutgoingMessageKind.Flow, true, WhatsAppOutgoingMessageState.Accepted)],
            outgoing.Select(item => (item.Kind, item.IsResend, item.State)));
        Assert.All(outgoing, item => Assert.Equal(WhatsAppMessageOrigin.Assistant, item.Origin));

        // One offer: the resend counts neither in the day nor in the list, and «Por qué lo mandó» goes only with the first Flow.
        var day = host.Clock.GetUtcNow().UtcDateTime;
        Assert.Equal(1, await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantReader>().CountOffersAsync(contact.ContactId, day.AddDays(-1), day.AddDays(1), Ct)));
        Assert.Single(await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantReader>().ListOffersAsync(contact.RecipientId, Ct)));
        using var client = host.Client(host.FirstOperator);
        using var thread = await client.GetAsync($"/api/whatsapp/chats/{contact.ContactId}/messages", Ct);
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        var flows = (await thread.ReadJsonAsync()).GetProperty("messages").EnumerateArray().Where(message => message.GetProperty("kind").GetString() == "Flow").ToArray();
        Assert.Equal([true, false], flows.Select(flow => flow.TryGetProperty("assistant", out var why) && why.ValueKind == System.Text.Json.JsonValueKind.Object));

        // The retention of the messages takes what was written with them; what the send took stays, without values.
        host.Clock.Advance(TimeSpan.FromDays(91));
        await host.Host.Services.GetRequiredService<WhatsAppMessageRetentionService>().ClearExpiredTextsAsync(Ct);
        var session = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppFlowSession>().AsNoTracking()
            .SingleAsync(item => item.ContactId == contact.ContactId, Ct));
        Assert.Null(session.ProtectedSharedValues);
        Assert.Equal([host.Forms.AddressFieldId], session.SharedLocation!.FieldIds);
    }

    [Fact]
    public async Task A_conversation_taken_before_the_location_arrives_gives_the_assistant_send_nothing()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, sharedQuestions: true);
        var contact = await SentAsync(host);
        await TakeAsync(host, contact.ContactId);
        var location = await AddLocationAsync(host, contact, "Bv. Oroño 1234, S2000 Rosario");

        Assert.Equal(0, (await ProcessAsync(host, contact.ContactId)).Value);

        Assert.Null(await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .Where(item => item.Id == location).Select(item => item.FlowSessionId).SingleAsync(Ct)));
        Assert.Single(await OutgoingAsync(host, contact.ContactId));
    }

    [Fact]
    public async Task A_conversation_taken_after_the_location_keeps_the_assistant_resend_from_going_out()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, sharedQuestions: true);
        var contact = await SentAsync(host);
        await AddLocationAsync(host, contact, null);
        Assert.Equal(1, (await ProcessAsync(host, contact.ContactId)).Value);
        await TakeAsync(host, contact.ContactId);

        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);

        Assert.IsType<WhatsAppFlowMessageResponse>(Assert.Single(host.Cloud.Messages));
        var resend = (await OutgoingAsync(host, contact.ContactId))[1];
        Assert.Equal((true, WhatsAppOutgoingMessageState.Failed), (resend.IsResend, resend.State));
    }

    // A6: the resend goes out as any message of its sender, so a send whose sender would no longer pass the reservation takes nothing
    // and what was shared stays in the chat: here the assistant's, once the account it answers for is no longer active.
    [Fact]
    public async Task A_send_whose_sender_would_not_pass_the_reservation_takes_nothing()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, sharedQuestions: true);
        var contact = await SentAsync(host);
        var session = await SessionAsync(host, contact.ContactId);
        await host.InTransactionAsync(async services =>
        {
            (await services.GetRequiredService<ApplicationDbContext>().Users.SingleAsync(user => user.Id == session.UserId, Ct)).SetActive(false);
            return true;
        }, Ct);
        var location = await AddLocationAsync(host, contact, "Bv. Oroño 1234, S2000 Rosario");

        Assert.Equal(0, (await ProcessAsync(host, contact.ContactId)).Value);

        Assert.Null(await TakenByAsync(host, location));
        Assert.Single(await OutgoingAsync(host, contact.ContactId));
    }

    // A7: the first reply is the one kept, so a resend whose send already has one does not go out, also when the reply worker attached
    // it and has not captured it yet (its files are downloading): it goes Failed without calling Meta.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_resend_whose_send_already_has_a_reply_goes_failed_without_calling_meta(bool captured)
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, sharedQuestions: true);
        var contact = await SentAsync(host);
        await AddLocationAsync(host, contact, "Bv. Oroño 1234, S2000 Rosario");
        Assert.Equal(1, (await ProcessAsync(host, contact.ContactId)).Value);
        await ReplyAsync(host, contact.ContactId, captured);

        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);

        Assert.IsType<WhatsAppFlowMessageResponse>(Assert.Single(host.Cloud.Messages));
        var resend = (await OutgoingAsync(host, contact.ContactId))[1];
        Assert.Equal((true, WhatsAppOutgoingMessageState.Failed), (resend.IsResend, resend.State));
    }

    // A2 and A7 in PostgreSQL: an open send is of the account and the link the number has today, Meta accepted its Flow (or a person
    // reviewed it as sent), it has no reply, captured or attached, it has not expired and it does not wait for its location.
    [Fact]
    public async Task Only_a_send_whose_form_went_out_and_has_no_reply_is_open()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, sharedQuestions: true);
        var contact = await SentAsync(host);
        var session = await SessionAsync(host, contact.ContactId);
        Assert.True(await OpenAsync());

        foreach (var (state, open) in new[] { (WhatsAppOutgoingMessageState.Pending, false), (WhatsAppOutgoingMessageState.Sending, false),
                     (WhatsAppOutgoingMessageState.Uncertain, false), (WhatsAppOutgoingMessageState.Failed, false),
                     (WhatsAppOutgoingMessageState.Reviewed, true), (WhatsAppOutgoingMessageState.Accepted, true) })
        {
            await ChangeAsync((_, flow, _) => Set(flow, nameof(WhatsAppOutgoingMessage.State), state));
            Assert.True(open == await OpenAsync(), state.ToString());
        }
        Assert.False(await OpenAsync(session.ExpiresAtUtc));
        await ChangeAsync((send, _, _) => Set(send, nameof(WhatsAppFlowSession.LocationState), WhatsAppFlowLocationState.Requested));
        Assert.False(await OpenAsync());
        await ChangeAsync((send, _, _) => Set(send, nameof(WhatsAppFlowSession.LocationState), WhatsAppFlowLocationState.None));
        Assert.True(await OpenAsync());
        await ChangeAsync((_, _, row) => Set(row, nameof(WhatsAppContact.LinkRevision), row.LinkRevision + 1));
        Assert.False(await OpenAsync());
        await ChangeAsync((_, _, row) => Set(row, nameof(WhatsAppContact.LinkRevision), row.LinkRevision - 1));
        await ChangeAsync((_, _, row) => Set(row, nameof(WhatsAppContact.UserId), (Guid?)host.FirstOperator));
        Assert.False(await OpenAsync());
        await ChangeAsync((_, _, row) => Set(row, nameof(WhatsAppContact.UserId), (Guid?)session.UserId));
        Assert.True(await OpenAsync());
        await ReplyAsync(host, contact.ContactId, captured: false);
        Assert.False(await OpenAsync());

        Task<bool> OpenAsync(DateTime? at = null) => host.ScopeAsync(async services =>
            (await services.GetRequiredService<IWhatsAppFlowSessionRepository>().ListOpenAsync(contact.ContactId, at ?? host.Clock.GetUtcNow().UtcDateTime, Ct))
                .Any(send => send.Session.Id == session.Id));
        Task<bool> ChangeAsync(Action<WhatsAppFlowSession, WhatsAppOutgoingMessage, WhatsAppContact> change) => host.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            change(await db.Set<WhatsAppFlowSession>().SingleAsync(item => item.Id == session.Id, Ct),
                await db.Set<WhatsAppOutgoingMessage>().SingleAsync(item => item.FlowSessionId == session.Id && item.Kind == WhatsAppOutgoingMessageKind.Flow, Ct),
                await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contact.ContactId, Ct));
            return true;
        }, Ct);
    }

    // The values go when the last location or contact the send took passes the retention of the messages, not the first one.
    [Fact]
    public async Task What_a_send_took_stays_while_the_last_shared_message_it_took_is_recent()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, sharedQuestions: true);
        var contact = await SentAsync(host);
        await AddLocationAsync(host, contact, "Bv. Oroño 1234, S2000 Rosario");
        Assert.Equal(1, (await ProcessAsync(host, contact.ContactId)).Value);
        host.Clock.Advance(TimeSpan.FromMinutes(10));
        await AddContactAsync(host, contact, "Ana Gómez");
        Assert.Equal(1, (await ProcessAsync(host, contact.ContactId)).Value);
        var retention = host.Host.Services.GetRequiredService<WhatsAppMessageRetentionService>();

        // 90 days after the location, and before the contact taken ten minutes later passes them, the values stay.
        host.Clock.Advance(TimeSpan.FromDays(90) - TimeSpan.FromMinutes(5));
        await retention.ClearExpiredTextsAsync(Ct);
        Assert.NotNull((await SessionAsync(host, contact.ContactId)).ProtectedSharedValues);
        host.Clock.Advance(TimeSpan.FromMinutes(10));
        await retention.ClearExpiredTextsAsync(Ct);
        Assert.Null((await SessionAsync(host, contact.ContactId)).ProtectedSharedValues);
    }

    // An unreadable value of what a send took leaves only that send out, logged by its type: the pass goes on and the contact stays in
    // the chat, without a resend.
    [Fact]
    public async Task A_send_whose_shared_values_cannot_be_read_takes_nothing_and_the_pass_goes_on()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, sharedQuestions: true);
        var contact = await SentAsync(host);
        await AddLocationAsync(host, contact, "Bv. Oroño 1234, S2000 Rosario");
        Assert.Equal(1, (await ProcessAsync(host, contact.ContactId)).Value);
        await host.InTransactionAsync(async services =>
        {
            (await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppFlowSession>().SingleAsync(item => item.ContactId == contact.ContactId, Ct))
                .SealSharedValues([1, 2, 3]);
            return true;
        }, Ct);
        var shared = await AddContactAsync(host, contact, "Ana Gómez");

        Assert.Equal(0, (await ProcessAsync(host, contact.ContactId)).Value);

        Assert.Null(await TakenByAsync(host, shared));
        Assert.Equal(2, (await OutgoingAsync(host, contact.ContactId)).Count);
    }

    // The assistant accepts its own proposal: its Flow goes out first, as the assistant's.
    private static async Task<WhatsAppAssistantContact> SentAsync(WhatsAppAssistantScenario host)
    {
        var contact = await host.ContactAsync(Ct, automatic: true);
        var work = await host.ScopeAsync(async services => (await services.GetRequiredService<IWhatsAppAssistantDispatchService>().PrepareAsync(contact.ContactId, Ct)).Value);
        await host.CompleteOutcomeAsync(contact.ContactId, host.Outcome(contact, Assert.IsType<WhatsAppAssistantWorkResponse>(work), "ready"), Ct);
        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        Assert.IsType<WhatsAppFlowMessageResponse>(Assert.Single(host.Cloud.Messages));
        return contact;
    }

    private static Task<bool> TakeAsync(WhatsAppAssistantScenario host, Guid contactId) => host.InTransactionAsync(async services =>
    {
        var row = await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().SingleAsync(item => item.Id == contactId, Ct);
        row.ChangeMode(WhatsAppConversationMode.Manual); return true;
    }, Ct);

    // The person shares a place, or a contact, some time after the Flow arrived, so what it sends goes after what is already queued.
    private static Task<Guid> AddLocationAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact row, string? address) =>
        AddSharedAsync(host, row, WhatsAppMessageKind.Location, message => message.RecordLocation(-32.947102m, -60.638955m, address));

    private static Task<Guid> AddContactAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact row, string name) =>
        AddSharedAsync(host, row, WhatsAppMessageKind.Contact, message => message.RecordContact(name, ["+54 9 341 555-0101"]));

    private static Task<Guid> AddSharedAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact row, WhatsAppMessageKind kind, Action<WhatsAppMessage> record) =>
        host.InTransactionAsync(async services =>
    {
        host.Clock.Advance(TimeSpan.FromSeconds(30));
        var db = services.GetRequiredService<ApplicationDbContext>();
        var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == row.ContactId, Ct);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var message = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), kind, null, null, now);
        record(message);
        message.Classify(WhatsAppMessagePurpose.Business); message.SetInboundSequence(contact.RegisterInboundMessage()); message.MarkProcessed(now);
        contact.RecordInbound(contact.WaId, contact.UserIdentifier, contact.ProfileName, now); db.Add(message);
        return message.Id;
    }, Ct);

    // The person answered the first Flow: the reply worker attached its reply and, when captured, completed the send with it.
    private static Task<Guid> ReplyAsync(WhatsAppAssistantScenario host, Guid contactId, bool captured) => host.InTransactionAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contactId, Ct);
        var session = await db.Set<WhatsAppFlowSession>().SingleAsync(item => item.ContactId == contactId, Ct);
        var reply = WhatsAppMessage.Inbound(contactId, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.FlowReply, null, null, host.Clock.GetUtcNow().UtcDateTime,
            [1, 2, 3]);
        reply.Classify(WhatsAppMessagePurpose.Business); reply.SetInboundSequence(contact.RegisterInboundMessage());
        reply.AttachFlowSession(session.Id);
        if (captured) session.CompleteCapture(reply.WaMessageId, WhatsAppFlowCaptureState.Completed, Guid.CreateVersion7(), "VIS-1", null);
        db.Add(reply);
        return reply.Id;
    }, Ct);

    private static Task<WhatsAppFlowSession> SessionAsync(WhatsAppAssistantScenario host, Guid contactId) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppFlowSession>().AsNoTracking()
            .SingleAsync(item => item.ContactId == contactId, Ct));

    private static Task<Guid?> TakenByAsync(WhatsAppAssistantScenario host, Guid messageId) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .Where(item => item.Id == messageId).Select(item => item.FlowSessionId).SingleAsync(Ct));

    private static void Set(object target, string property, object? value) => target.GetType().GetProperty(property)!.SetValue(target, value);

    private static Task<ArquitecturaBase.Domain.Results.Result<int>> ProcessAsync(WhatsAppAssistantScenario host, Guid contactId) =>
        host.ScopeAsync(services => services.GetRequiredService<IWhatsAppFlowLocationService>().ProcessAsync(contactId, Ct));

    private static Task<List<WhatsAppOutgoingMessage>> OutgoingAsync(WhatsAppAssistantScenario host, Guid contactId) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(item => item.ContactId == contactId).OrderBy(item => item.RequestedAtUtc).ThenBy(item => item.Id).ToListAsync(Ct));
}
