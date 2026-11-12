using System.Net;
using System.Net.Http.Json;
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

// W1-A, sent by the assistant (asked by the user on 2026-11-11): a form with a location opens with the assistant's location
// request; the location sends its Flow, also the assistant's, and the send is one offer. A conversation taken while it waits,
// or a content that expired, keeps that Flow from going out, as any message of the assistant still on its way.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantLocationIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // In Atención manual the assistant only suggests: who attends sends the suggestion, which opens with the location request as a
    // send of that person; the suggestion reads Sent with it. With an optional location the skip button of that person goes out
    // too, keeps its title in the chat, and its tap sends that person's Flow (the button failed to complete since 7cadd0ea).
    [Fact]
    public async Task Who_attends_sends_the_suggestion_and_it_opens_with_the_location_request_of_that_person()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, optionalLocation: true);
        using var client = host.Client(host.FirstOperator);
        var contact = await host.ContactAsync(Ct); var requestId = Guid.NewGuid();
        var route = $"/api/whatsapp/chats/{contact.ContactId}/assistant-suggestions";
        using (var requested = await client.PostAsJsonAsync(route, new { requestId, sourceMessageId = contact.SourceMessageId, expectedModeRevision = 0 }, Ct))
            Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        await host.CompleteAsync(contact, requestId, "ready", Ct);

        using var sent = await client.PostAsJsonAsync(route + $"/{requestId}/send", new { requestId = Guid.NewGuid(), includedFieldIds = new[] { host.Forms.FieldId } }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        var outgoing = await sent.ReadJsonAsync();
        Assert.Equal(("LocationRequest", "Conversation", host.FirstOperator), (outgoing.GetProperty("kind").GetString(), outgoing.GetProperty("origin").GetString(),
            outgoing.GetProperty("operatorUserId").GetGuid()));
        Assert.Equal("Requested", outgoing.GetProperty("form").GetProperty("location").GetProperty("state").GetString());
        using var read = await client.GetAsync(route + $"/{requestId}", Ct);
        var suggestion = await read.ReadJsonAsync();
        Assert.Equal(("Sent", "LocationRequest"), (suggestion.GetProperty("state").GetString(), suggestion.GetProperty("outgoing").GetProperty("kind").GetString()));

        var worker = host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        await worker.ProcessNextAsync(Ct); await worker.ProcessNextAsync(Ct);
        Assert.Equal([typeof(WhatsAppLocationRequestMessage), typeof(WhatsAppReplyButtonsMessage)], host.Cloud.Messages.Select(item => item.GetType()));
        var titles = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .Where(item => item.ContactId == contact.ContactId && item.Direction == WhatsAppMessageDirection.Outbound && item.Kind == WhatsAppMessageKind.Interactive)
            .Select(item => new { item.QuickReplyTitlesJson, item.FlowSessionId }).SingleAsync(Ct));
        Assert.Contains("Seguir sin ubicaci", titles.QuickReplyTitlesJson);
        Assert.Null(titles.FlowSessionId);

        await AddSkipTapAsync(host, contact);
        await ResolveAsync(host, contact.ContactId);
        await worker.ProcessNextAsync(Ct);

        Assert.IsType<WhatsAppFlowMessageResponse>(host.Cloud.Messages[2]);
        Assert.Equal(WhatsAppFlowLocationState.Skipped, await LocationStateAsync(host, contact.ContactId));
        var flow = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(item => item.ContactId == contact.ContactId && item.Kind == WhatsAppOutgoingMessageKind.Flow)
            .Select(item => new { item.Origin, item.OperatorUserId, item.State }).SingleAsync(Ct));
        Assert.Equal((WhatsAppMessageOrigin.Conversation, (Guid?)host.FirstOperator, WhatsAppOutgoingMessageState.Accepted), (flow.Origin, flow.OperatorUserId, flow.State));
    }

    [Fact]
    public async Task The_assistant_asks_for_the_location_first_and_its_flow_follows_as_one_offer()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, withLocation: true);
        var contact = await RequestSentAsync(host);
        Assert.True(await UnresolvedOfferAsync(host, contact.ContactId, Guid.NewGuid()) is false);

        await AddLocationAsync(host, contact);
        await ResolveAsync(host, contact.ContactId);
        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);

        Assert.Equal(2, host.Cloud.Messages.Count);
        Assert.IsType<WhatsAppFlowMessageResponse>(host.Cloud.Messages[1]);
        Assert.Equal([(WhatsAppOutgoingMessageKind.LocationRequest, WhatsAppOutgoingMessageState.Accepted),
            (WhatsAppOutgoingMessageKind.Flow, WhatsAppOutgoingMessageState.Accepted)], await OutgoingAsync(host, contact.ContactId));
        Assert.Equal(WhatsAppFlowLocationState.Received, await LocationStateAsync(host, contact.ContactId));
        var day = host.Clock.GetUtcNow().UtcDateTime;
        Assert.Equal(1, await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantReader>()
            .CountOffersAsync(contact.ContactId, day.AddDays(-1), day.AddDays(1), Ct)));
    }

    // An optional location adds «Seguir sin ubicación», also the assistant's: it finds its reference through the send, and its tap
    // sends the Flow without the location.
    [Fact]
    public async Task An_optional_location_also_sends_the_assistant_skip_button_and_its_tap_sends_the_flow()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, optionalLocation: true);
        var contact = await host.ContactAsync(Ct, automatic: true);
        var work = await host.ScopeAsync(async services => (await services.GetRequiredService<IWhatsAppAssistantDispatchService>().PrepareAsync(contact.ContactId, Ct)).Value);
        await host.CompleteOutcomeAsync(contact.ContactId, host.Outcome(contact, Assert.IsType<WhatsAppAssistantWorkResponse>(work), "ready"), Ct);
        var worker = host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        await worker.ProcessNextAsync(Ct); await worker.ProcessNextAsync(Ct);

        Assert.Equal([typeof(WhatsAppLocationRequestMessage), typeof(WhatsAppReplyButtonsMessage)], host.Cloud.Messages.Select(item => item.GetType()));
        Assert.Equal([(WhatsAppOutgoingMessageKind.LocationRequest, WhatsAppOutgoingMessageState.Accepted),
            (WhatsAppOutgoingMessageKind.QuickReplies, WhatsAppOutgoingMessageState.Accepted)], await OutgoingAsync(host, contact.ContactId));

        await AddSkipTapAsync(host, contact);
        await ResolveAsync(host, contact.ContactId);
        await worker.ProcessNextAsync(Ct);

        Assert.IsType<WhatsAppFlowMessageResponse>(host.Cloud.Messages[2]);
        Assert.Equal((WhatsAppOutgoingMessageKind.Flow, WhatsAppOutgoingMessageState.Accepted), (await OutgoingAsync(host, contact.ContactId))[2]);
        Assert.Equal(WhatsAppFlowLocationState.Skipped, await LocationStateAsync(host, contact.ContactId));
        var day = host.Clock.GetUtcNow().UtcDateTime;
        Assert.Equal(1, await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantReader>()
            .CountOffersAsync(contact.ContactId, day.AddDays(-1), day.AddDays(1), Ct)));
    }

    [Fact]
    public async Task Taking_the_conversation_while_it_waits_keeps_the_assistant_flow_from_going_out()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, withLocation: true);
        var contact = await RequestSentAsync(host);
        await host.InTransactionAsync(async services =>
        {
            var row = await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().SingleAsync(item => item.Id == contact.ContactId, Ct);
            row.ChangeMode(WhatsAppConversationMode.Manual); return true;
        }, Ct);

        await AddLocationAsync(host, contact);
        await ResolveAsync(host, contact.ContactId);
        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);

        Assert.IsType<WhatsAppLocationRequestMessage>(Assert.Single(host.Cloud.Messages));
        Assert.Equal((WhatsAppOutgoingMessageKind.Flow, WhatsAppOutgoingMessageState.Failed), (await OutgoingAsync(host, contact.ContactId))[1]);
    }

    [Fact]
    public async Task An_expired_assistant_content_keeps_its_flow_from_going_out()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, withLocation: true);
        var contact = await RequestSentAsync(host);
        await AddLocationAsync(host, contact);
        await ResolveAsync(host, contact.ContactId);
        var expiry = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppFlowSession>().AsNoTracking()
            .Where(item => item.ContactId == contact.ContactId).Select(item => item.AssistantContentExpiresAtUtc).SingleAsync(Ct));

        host.Clock.SetUtcNow(new DateTimeOffset(expiry!.Value.AddMinutes(1)));
        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);

        Assert.IsType<WhatsAppLocationRequestMessage>(Assert.Single(host.Cloud.Messages));
        Assert.Equal((WhatsAppOutgoingMessageKind.Flow, WhatsAppOutgoingMessageState.Failed), (await OutgoingAsync(host, contact.ContactId))[1]);
    }

    // The assistant accepts its own proposal of the form with a location: the location request goes out first, as the assistant's.
    private static async Task<WhatsAppAssistantContact> RequestSentAsync(WhatsAppAssistantScenario host)
    {
        var contact = await host.ContactAsync(Ct, automatic: true);
        var work = await host.ScopeAsync(async services => (await services.GetRequiredService<IWhatsAppAssistantDispatchService>().PrepareAsync(contact.ContactId, Ct)).Value);
        await host.CompleteOutcomeAsync(contact.ContactId, host.Outcome(contact, Assert.IsType<WhatsAppAssistantWorkResponse>(work), "ready"), Ct);
        Assert.True(await UnresolvedOfferAsync(host, contact.ContactId, Guid.NewGuid()));
        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        Assert.IsType<WhatsAppLocationRequestMessage>(Assert.Single(host.Cloud.Messages));
        Assert.Equal([(WhatsAppOutgoingMessageKind.LocationRequest, WhatsAppOutgoingMessageState.Accepted)], await OutgoingAsync(host, contact.ContactId));
        var origins = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(item => item.ContactId == contact.ContactId).Select(item => item.Origin).ToListAsync(Ct));
        Assert.All(origins, origin => Assert.Equal(WhatsAppMessageOrigin.Assistant, origin));
        return contact;
    }

    private static Task<bool> UnresolvedOfferAsync(WhatsAppAssistantScenario host, Guid contactId, Guid excludingRequestId) =>
        host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantReader>().ExistsUnresolvedOfferAsync(contactId, excludingRequestId, Ct));

    private static Task<Guid> AddLocationAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact row) => host.InTransactionAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == row.ContactId, Ct);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var message = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Location, null, null, now);
        message.RecordLocation(-34.603722m, -58.381592m);
        message.Classify(WhatsAppMessagePurpose.Business); message.SetInboundSequence(contact.RegisterInboundMessage()); message.MarkProcessed(now);
        contact.RecordInbound(contact.WaId, contact.UserIdentifier, contact.ProfileName, now); db.Add(message);
        return message.Id;
    }, Ct);

    // A tap on «Seguir sin ubicación» of the contact's one send.
    private static async Task AddSkipTapAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact contact)
    {
        var sessionId = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppFlowSession>().AsNoTracking()
            .Where(item => item.ContactId == contact.ContactId).Select(item => item.Id).SingleAsync(Ct));
        await host.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var row = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contact.ContactId, Ct);
            var now = host.Clock.GetUtcNow().UtcDateTime;
            var tap = WhatsAppMessage.Inbound(row.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.ButtonReply, "Seguir sin ubicación",
                WhatsAppFlowLocationReply.Id(sessionId), now);
            tap.Classify(WhatsAppMessagePurpose.Business); tap.SetInboundSequence(row.RegisterInboundMessage()); tap.MarkProcessed(now);
            row.RecordInbound(row.WaId, row.UserIdentifier, row.ProfileName, now); db.Add(tap); return true;
        }, Ct);
    }

    private static async Task ResolveAsync(WhatsAppAssistantScenario host, Guid contactId)
    {
        var resolved = await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppFlowLocationService>().ProcessAsync(contactId, Ct));
        Assert.Equal(1, resolved.Value);
    }

    private static Task<List<(WhatsAppOutgoingMessageKind Kind, WhatsAppOutgoingMessageState State)>> OutgoingAsync(WhatsAppAssistantScenario host, Guid contactId) =>
        host.ScopeAsync(async services => (await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(item => item.ContactId == contactId).OrderBy(item => item.RequestedAtUtc).ThenBy(item => item.Id)
            .Select(item => new { item.Kind, item.State }).ToListAsync(Ct)).Select(item => (item.Kind, item.State)).ToList());

    private static Task<WhatsAppFlowLocationState> LocationStateAsync(WhatsAppAssistantScenario host, Guid contactId) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppFlowSession>().AsNoTracking()
            .Where(item => item.ContactId == contactId).Select(item => item.LocationState).SingleAsync(Ct));
}
