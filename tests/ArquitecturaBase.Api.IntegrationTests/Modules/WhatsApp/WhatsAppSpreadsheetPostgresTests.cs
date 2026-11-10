using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// Stage 2 of the assistant authoring with PostgreSQL: a spreadsheet goes from the signed webhook to its private copy and its prepared
// sheet; one that is not read keeps its reason and is answered once; anyone else's is not downloaded. Meta's download and the private
// storage are the probe's; the owner's reader is BusinessForms' through the neutral port.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppSpreadsheetPostgresTests(ApiFactory factory)
{
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly byte[] Csv = Encoding.UTF8.GetBytes("Código,Texto\nA1,Uno\nA2,Dos\n");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_spreadsheet_from_the_webhook_is_downloaded_prepared_and_copied_and_the_chat_still_shows_it_as_media()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv };
        await using var host = await CreateAsync(probe);
        var person = await host.ContactAsync(Ct, automatic: true);
        await GrantCatalogsAsync(host, person.RecipientId);
        var waMessageId = await ReceiveAsync(host, person, probe, "clientes.csv", "text/csv", "Armame un catálogo con esta planilla");

        await ProcessAsync(host);

        var message = await MessageAsync(host, waMessageId);
        Assert.Equal((WhatsAppMessageKind.Spreadsheet, "Armame un catálogo con esta planilla", WhatsAppSpreadsheetState.Stored),
            (message.Kind, message.Body, message.SpreadsheetState));
        Assert.Equal(($"whatsapp/documents/{message.Id:N}.csv", (long?)Csv.Length, Convert.ToHexStringLower(SHA256.HashData(Csv))),
            (message.SpreadsheetStorageKey, message.SpreadsheetSize, message.SpreadsheetCopySha256));
        Assert.Equal(Csv, probe.Stored(message.SpreadsheetStorageKey!));
        Assert.Equal([WhatsAppMessage.MaxSpreadsheetBytes], probe.Limits);
        var prepared = JsonSerializer.Deserialize<StructuredSpreadsheet>(message.SpreadsheetPreparedJson!, JsonSerializerOptions.Web)!;
        Assert.Equal(("clientes.csv", StructuredSpreadsheetKind.Csv, 3, 2), (prepared.FileName, prepared.Kind, prepared.RowCount, prepared.ColumnCount));
        Assert.Equal("Uno", prepared.Rows[1].Cells["B"]);

        // The assistant does not take it yet: it is not a source and, being the last message, the earlier text is not interpreted.
        var assistant = await host.ScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IWhatsAppAssistantReader>();
            return (Source: await reader.FindLatestSourceAsync(person.ContactId, Ct),
                Pending: await reader.ListPendingContactsAsync([], 50, host.Clock.GetUtcNow().UtcDateTime, Ct));
        });
        Assert.Null(assistant.Source);
        Assert.DoesNotContain(person.ContactId, assistant.Pending);
        Assert.Empty(await ReferencesAsync(host, person.ContactId));

        // The chat of today keeps showing it as the media it was, without its caption or its name, also in the inbox.
        using var client = host.Client(host.FirstOperator);
        using var threadResponse = await client.GetAsync($"/api/whatsapp/chats/{person.ContactId}/messages", Ct);
        Assert.Equal(HttpStatusCode.OK, threadResponse.StatusCode);
        var thread = await threadResponse.ReadJsonAsync();
        var shown = thread.GetProperty("messages").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == message.Id);
        Assert.Equal(("Media", JsonValueKind.Null), (shown.GetProperty("kind").GetString(), shown.GetProperty("body").ValueKind));
        Assert.DoesNotContain("clientes", thread.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("Armame", thread.GetRawText(), StringComparison.Ordinal);
        using var listResponse = await client.GetAsync("/api/whatsapp/chats?pageSize=100", Ct);
        var chat = (await listResponse.ReadJsonAsync()).GetProperty("page").GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == person.ContactId);
        Assert.Equal(JsonValueKind.Null, chat.GetProperty("preview").ValueKind);
    }

    [Fact]
    public async Task An_unreadable_spreadsheet_keeps_its_reason_without_a_copy_and_is_answered_once_in_the_person_language()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Encoding.Latin1.GetBytes("Código;Texto\nA1;Año\n") };
        await using var host = await CreateAsync(probe);
        var person = await host.ContactAsync(Ct, automatic: true);
        await GrantCatalogsAsync(host, person.RecipientId);
        var waMessageId = await ReceiveAsync(host, person, probe, "clientes.csv", "text/plain", null);

        await ProcessAsync(host);
        await ProcessAsync(host);

        var message = await MessageAsync(host, waMessageId);
        Assert.Equal((WhatsAppSpreadsheetState.Rejected, "unreadable", (string?)null),
            (message.SpreadsheetState, message.SpreadsheetFailureCode, message.SpreadsheetStorageKey));
        Assert.Empty(probe.Keys);
        Assert.Equal(1, probe.Downloads);
        var reply = Assert.Single(await OutgoingAsync(host, person.ContactId));
        Assert.Equal((WhatsAppMessageOrigin.Assistant, WhatsAppOutgoingMessageKind.Text, person.RecipientId), (reply.Origin, reply.Kind, reply.RecipientUserId));
        Assert.Equal("No puedo leer la planilla. Guardala como Libro de Excel (.xlsx), o un CSV como CSV UTF-8, y mandala de nuevo.", reply.Body);
        var reference = Assert.Single(await ReferencesAsync(host, person.ContactId));
        Assert.Equal((message.Id, WhatsAppAssistantRequestState.Sent, (Guid?)reply.Id), (reference.SourceMessageId, reference.State, reference.OutgoingId));

        // It leaves through the outbox like any other reply of the assistant, once.
        var outbox = host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        await outbox.ProcessNextAsync(Ct);
        await outbox.ProcessNextAsync(Ct);
        Assert.Equal(reply.Body, Assert.IsType<WhatsAppTextMessage>(Assert.Single(host.Cloud.Messages)).Body);
    }

    // The rejection's fixed text names its own request as the proposal: the reader leaves it out of the conversation, of the offers and
    // of the quota. A question of an interpretation created before it still waits for the person's answer.
    [Fact]
    public async Task A_rejection_is_no_turn_of_the_conversation_no_unresolved_offer_and_no_offer_of_the_day()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Encoding.Latin1.GetBytes("Código;Texto\nA1;Año\n") };
        await using var host = await CreateAsync(probe);
        var person = await host.ContactAsync(Ct, automatic: true);
        await GrantCatalogsAsync(host, person.RecipientId);
        var before = host.Clock.GetUtcNow().UtcDateTime;
        host.Clock.Advance(TimeSpan.FromSeconds(5));
        await ReceiveAsync(host, person, probe, "clientes.csv", "text/plain", null);

        await ProcessAsync(host);

        var rejection = Assert.Single(await ReferencesAsync(host, person.ContactId));
        Assert.Equal(((Guid?)rejection.RequestId, (Guid?)null), (rejection.ProposalId, rejection.ChangeRequestId));
        // An interpretation created before the rejection asks what to build only once the rejection exists.
        var interpretation = await host.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var contact = await db.Set<WhatsAppContact>().AsNoTracking().SingleAsync(row => row.Id == person.ContactId, Ct);
            var row = WhatsAppAssistantRequest.Create(Guid.CreateVersion7(), contact.Id, person.SourceMessageId, person.RecipientId, person.RecipientId,
                WhatsAppAssistantRequestKind.Automatic, contact.ModeRevision, contact.LinkRevision, "{\"channelId\":\"whatsapp-assistant\"}", before, before.AddDays(1));
            Assert.True(row.AwaitAnswer());
            db.Add(row);
            return row.RequestId;
        }, Ct);
        var day = host.Clock.GetUtcNow().UtcDateTime.Date;
        var reads = await host.ScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IWhatsAppAssistantReader>();
            return (Change: await reader.FindChangeRequestAsync(person.ContactId, Ct), Unresolved: await reader.ExistsUnresolvedOfferAsync(person.ContactId, Guid.NewGuid(), Ct),
                Offers: await reader.CountOffersAsync(person.ContactId, day, day.AddDays(1), Ct));
        });
        Assert.Equal(((Guid?)interpretation, false, 0), reads);
        Assert.Equal(WhatsAppOutgoingMessageState.Pending, Assert.Single(await OutgoingAsync(host, person.ContactId)).State);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Anyone_else_keeps_it_as_today_without_downloading_it(bool automatic, bool mayAttach)
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Csv };
        await using var host = await CreateAsync(probe);
        var person = await host.ContactAsync(Ct, automatic);
        if (mayAttach) await GrantCatalogsAsync(host, person.RecipientId);
        var waMessageId = await ReceiveAsync(host, person, probe, "clientes.xlsx", XlsxType, "Armame un catálogo");

        await ProcessAsync(host);

        var message = await MessageAsync(host, waMessageId);
        Assert.Equal((WhatsAppMessageKind.Spreadsheet, WhatsAppSpreadsheetState.Unavailable, "clientes.xlsx"),
            (message.Kind, message.SpreadsheetState, message.SpreadsheetFileName));
        Assert.Equal(0, probe.Downloads);
        Assert.Empty(await OutgoingAsync(host, person.ContactId));
    }

    [Fact]
    public async Task A_failed_download_waits_its_turn_and_only_a_pending_spreadsheet_is_scanned()
    {
        await using var host = await CreateAsync(new WhatsAppDocumentProbe());
        var person = await host.ContactAsync(Ct, automatic: true);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var messageId = await AddSpreadsheetAsync(host, person.ContactId, now, message => message.RegisterSpreadsheetFailure(now, "Files.Content.Unavailable"));

        Assert.False(await PendingAsync(host, person.ContactId, now));
        Assert.False(await PendingAsync(host, person.ContactId, now.AddSeconds(29)));
        Assert.True(await PendingAsync(host, person.ContactId, now.AddSeconds(30)));

        await host.InTransactionAsync(async services =>
        {
            (await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().SingleAsync(row => row.Id == messageId, Ct))
                .SetSpreadsheetUnavailable("Files.Content.Unavailable");
            return true;
        }, Ct);
        Assert.False(await PendingAsync(host, person.ContactId, now.AddMinutes(1)));
    }

    [Fact]
    public async Task Retention_takes_the_descriptor_the_copy_and_the_prepared_sheet_with_the_body_and_keeps_only_live_copies()
    {
        await using var host = await CreateAsync(new WhatsAppDocumentProbe());
        var person = await host.ContactAsync(Ct, automatic: true);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var old = now.AddDays(-91);
        var stored = await AddSpreadsheetAsync(host, person.ContactId, now, message => Store(message));
        var pending = await AddSpreadsheetAsync(host, person.ContactId, now, _ => { });
        var rejected = await AddSpreadsheetAsync(host, person.ContactId, now, message => message.RejectSpreadsheet("empty"));
        var expired = await AddSpreadsheetAsync(host, person.ContactId, old, message => Store(message));
        string[] keys = [$"whatsapp/documents/{stored:N}.csv", $"whatsapp/documents/{pending:N}.xlsx", $"whatsapp/documents/{rejected:N}.csv",
            $"whatsapp/documents/{expired:N}.csv"];

        var referenced = await host.ScopeAsync(services => services.GetServices<IPrivateFileRetentionSource>()
            .OfType<WhatsAppSpreadsheetRetentionSource>().Single().ListReferencedKeysAsync(keys, Ct));
        Assert.Equal(keys[..2].Order(StringComparer.Ordinal), referenced.Order(StringComparer.Ordinal));

        await host.Host.Services.GetRequiredService<WhatsAppMessageRetentionService>().ClearExpiredTextsAsync(Ct);

        var cleared = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .SingleAsync(row => row.Id == expired, Ct));
        Assert.All(new[] { cleared.Body, cleared.SpreadsheetMediaId, cleared.SpreadsheetContentType, cleared.SpreadsheetSha256, cleared.SpreadsheetFileName,
            cleared.SpreadsheetStorageKey, cleared.SpreadsheetCopySha256, cleared.SpreadsheetPreparedJson }, value => Assert.Null(value));
        Assert.Null(cleared.SpreadsheetSize);
        Assert.Equal(WhatsAppSpreadsheetState.Stored, cleared.SpreadsheetState);
        var live = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .SingleAsync(row => row.Id == stored, Ct));
        Assert.Equal(("Armame un catálogo", "clientes.csv"), (live.Body, live.SpreadsheetFileName));
        Assert.NotNull(live.SpreadsheetPreparedJson);

        static void Store(WhatsAppMessage message) =>
            Assert.True(message.StoreSpreadsheet($"whatsapp/documents/{message.Id:N}.csv", 3, new string('b', 64), "{\"rowCount\":1}").IsSuccess);
    }

    private Task<WhatsAppAssistantScenario> CreateAsync(WhatsAppDocumentProbe probe) =>
        WhatsAppAssistantScenario.CreateAsync(factory, Ct, builder => builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IPrivateFileStore>(probe));
            services.Replace(ServiceDescriptor.Singleton<IWhatsAppMediaClient>(probe));
        }));

    // The person manages catalogs, which is who may attach a spreadsheet in this stage.
    private static Task<bool> GrantCatalogsAsync(WhatsAppAssistantScenario host, Guid userId) => host.InTransactionAsync(async services =>
    {
        var role = "spreadsheet-" + Guid.NewGuid().ToString("N");
        await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, ["formrecords.create", "formcatalogs.read", "formcatalogs.manage"], Ct);
        await services.GetRequiredService<IUserRepository>().SetRolesAsync(userId, [role], Ct);
        return true;
    }, Ct);

    private static async Task<string> ReceiveAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact person, WhatsAppDocumentProbe probe,
        string fileName, string type, string? caption)
    {
        var contact = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().AsNoTracking()
            .SingleAsync(row => row.Id == person.ContactId, Ct));
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        var body = MetaWebhook.Build(contacts: [MetaWebhook.Contact(contact.WaId, contact.UserIdentifier, contact.ProfileName)],
            messages: [MetaWebhook.Document(waMessageId, contact.WaId, contact.UserIdentifier, host.Clock.GetUtcNow().UtcDateTime, fileName, type, caption,
                sha256: Convert.ToBase64String(SHA256.HashData(probe.Bytes)))]);
        using var client = host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return waMessageId;
    }

    private static Task ProcessAsync(WhatsAppAssistantScenario host) =>
        host.Host.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);

    private static Task<WhatsAppMessage> MessageAsync(WhatsAppAssistantScenario host, string waMessageId) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .SingleAsync(row => row.WaMessageId == waMessageId, Ct));

    private static Task<List<WhatsAppOutgoingMessage>> OutgoingAsync(WhatsAppAssistantScenario host, Guid contactId) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(row => row.ContactId == contactId).ToListAsync(Ct));

    private static Task<List<WhatsAppAssistantRequest>> ReferencesAsync(WhatsAppAssistantScenario host, Guid contactId) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .Where(row => row.ContactId == contactId).ToListAsync(Ct));

    private static Task<bool> PendingAsync(WhatsAppAssistantScenario host, Guid contactId, DateTime nowUtc) => host.ScopeAsync(async services =>
    {
        var reader = services.GetRequiredService<IWhatsAppSpreadsheetReader>();
        var found = await reader.FindPendingAsync(contactId, nowUtc, Ct) is not null;
        Assert.Equal(found, (await reader.ListPendingContactsAsync([], 50, nowUtc, Ct)).Contains(contactId));
        return found;
    });

    private static Task<Guid> AddSpreadsheetAsync(WhatsAppAssistantScenario host, Guid contactId, DateTime occurredAtUtc, Action<WhatsAppMessage> arrange) =>
        host.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var contact = await db.Set<WhatsAppContact>().SingleAsync(row => row.Id == contactId, Ct);
            var message = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Spreadsheet, "Armame un catálogo", null, occurredAtUtc);
            Assert.True(message.SetSpreadsheetDescriptor("1234", "text/csv", new string('a', 64), "clientes.csv"));
            message.Classify(WhatsAppMessagePurpose.Business);
            message.SetInboundSequence(contact.RegisterInboundMessage());
            message.MarkProcessed(occurredAtUtc);
            arrange(message);
            db.Add(message);
            return message.Id;
        }, Ct);
}
