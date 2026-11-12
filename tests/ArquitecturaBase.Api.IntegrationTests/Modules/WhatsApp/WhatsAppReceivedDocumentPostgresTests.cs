using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// Task 8 of the documents with PostgreSQL: a PDF goes from the signed webhook to its private copy and the engine's reading, kept with the
// message; a file that cannot be read keeps its reason, is answered once and goes to the inbox discarded; an unlinked number, an account
// that can neither load nor build forms and anyone else are not downloaded. Meta's download and the private storage are the probe's, the
// engine is the scenario's double and the inbox a neutral capturing owner; the owner's reader is BusinessForms' through the neutral port.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppReceivedDocumentPostgresTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly StructuredDocumentReading Reading = new(StructuredDocumentReadability.Readable, null, "es", "Transferencia $ 150.000", false);

    [Fact]
    public async Task A_pdf_from_the_webhook_is_copied_and_read_by_the_engine_and_its_reading_stays_with_the_message()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var inbox = new CapturingStructuredDocumentChannel();
        await using var host = await CreateAsync(probe, inbox);
        host.Operations.AllowSubmit = true;
        var person = await host.ContactAsync(Ct, automatic: true);
        var waMessageId = await ReceiveAsync(host, person, probe, "comprobante.pdf", "application/pdf", null);

        await ProcessAsync(host);

        var message = await MessageAsync(host, waMessageId);
        Assert.Equal((WhatsAppMessageKind.ReceivedDocument, (string?)null, WhatsAppDocumentState.Reading),
            (message.Kind, message.Body, message.DocumentState));
        Assert.Equal(($"whatsapp/documents/{message.Id:N}.pdf", (long?)probe.Bytes.Length, Convert.ToHexStringLower(SHA256.HashData(probe.Bytes)), (int?)1),
            (message.DocumentStorageKey, message.DocumentSize, message.DocumentCopySha256, message.DocumentPages));
        Assert.Equal(probe.Bytes, probe.Stored(message.DocumentStorageKey!));
        Assert.Equal([StructuredAttachmentLimits.MaximumBytes], probe.Limits);
        var submitted = Assert.Single(host.Operations.SubmitRequests);
        Assert.Equal((message.DocumentRequestId!.Value, "whatsapp-assistant", person.RecipientId, (Guid?)person.RecipientId),
            (submitted.RequestId, submitted.Context.ChannelId, submitted.Context.ActorUserId, submitted.Context.SubjectUserId));
        Assert.Equal(new AssistantDocumentInput(message.DocumentStorageKey!, "comprobante.pdf", "application/pdf", probe.Bytes.Length, message.DocumentCopySha256!, 1,
            message.OccurredAtUtc.AddDays(90)), submitted.Document);

        host.Operations.Complete(new AssistantOperationResponse(submitted.RequestId, "completed", null, null, null) { DocumentReading = Reading });
        await ProcessAsync(host);

        var read = await MessageAsync(host, waMessageId);
        Assert.Equal(WhatsAppDocumentState.Read, read.DocumentState);
        Assert.Equal(Reading, JsonSerializer.Deserialize<StructuredDocumentReading>(read.DocumentReadingJson!, WhatsAppReceivedDocumentReadingPolicy.ReadingJson));
        Assert.Single(host.Operations.SubmitRequests);
        Assert.Empty(inbox.Registrations);
        Assert.Empty(await OutgoingAsync(host, person.ContactId));
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_keeps_its_reason_is_answered_once_and_goes_to_the_inbox_discarded()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Encoding.ASCII.GetBytes("%PDF-1.7\nnot really a document\n") };
        var inbox = new CapturingStructuredDocumentChannel();
        await using var host = await CreateAsync(probe, inbox);
        var person = await host.ContactAsync(Ct, automatic: true);
        var waMessageId = await ReceiveAsync(host, person, probe, "comprobante.pdf", "application/pdf", "Cargalo en pagos");

        await ProcessAsync(host);
        await ProcessAsync(host);

        var message = await MessageAsync(host, waMessageId);
        Assert.Equal((WhatsAppDocumentState.Rejected, "unreadable", (string?)null), (message.DocumentState, message.DocumentFailureCode, message.DocumentStorageKey));
        Assert.Equal(1, probe.Downloads);
        var reply = Assert.Single(await OutgoingAsync(host, person.ContactId));
        Assert.Equal((WhatsAppMessageOrigin.Assistant, "No pude abrir el archivo. Mandámelo de nuevo como PDF o como foto."), (reply.Origin, reply.Body));
        var reference = Assert.Single(await ReferencesAsync(host, person.ContactId));
        Assert.Equal((message.Id, WhatsAppAssistantRequestState.Sent, (Guid?)reply.Id), (reference.SourceMessageId, reference.State, reference.OutgoingId));
        var registered = Assert.Single(inbox.Registrations);
        Assert.Equal(("whatsapp", message.Id.ToString("N"), person.RecipientId, StructuredDocumentNotRead.Rejected, (StructuredAttachmentRejection?)StructuredAttachmentRejection.Unreadable),
            (registered.ChannelId, registered.SourceId, registered.SenderUserId, registered.NotRead, registered.Rejection));
        Assert.Equal(($"whatsapp/documents/{message.Id:N}.pdf", "application/pdf"), (registered.Copy!.StorageKey, registered.Copy.MediaType));
        Assert.Empty(host.Operations.SubmitRequests);

        // It leaves through the outbox like any other reply of the assistant, once.
        var outbox = host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        await outbox.ProcessNextAsync(Ct);
        await outbox.ProcessNextAsync(Ct);
        Assert.Equal(reply.Body, Assert.IsType<WhatsAppTextMessage>(Assert.Single(host.Cloud.Messages)).Body);
    }

    [Fact]
    public async Task An_unlinked_number_is_neither_downloaded_nor_answered_and_the_chat_says_why()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var inbox = new CapturingStructuredDocumentChannel();
        await using var host = await CreateAsync(probe, inbox);
        var person = await host.ContactAsync(Ct, automatic: true);
        await host.InTransactionAsync(async services =>
        {
            var contact = await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().SingleAsync(row => row.Id == person.ContactId, Ct);
            contact.UnlinkUser();
            return true;
        }, Ct);
        var waMessageId = await ReceiveAsync(host, person, probe, "comprobante.pdf", "application/pdf", "Cargalo en pagos");

        await ProcessAsync(host);

        var message = await MessageAsync(host, waMessageId);
        Assert.Equal((WhatsAppDocumentState.Unavailable, "notLinked"), (message.DocumentState, message.DocumentFailureCode));
        Assert.Equal(0, probe.Downloads);
        Assert.Empty(await OutgoingAsync(host, person.ContactId));
        Assert.Empty(inbox.Registrations);
        using var client = host.Client(host.FirstOperator);
        using var thread = await client.GetAsync($"/api/whatsapp/chats/{person.ContactId}/messages", Ct);
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        var shown = (await thread.ReadJsonAsync()).GetProperty("messages").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == message.Id);
        Assert.Equal("notLinked", shown.GetProperty("document").GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task A_linked_account_that_can_neither_load_nor_build_forms_is_told_and_handed_to_a_person()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var inbox = new CapturingStructuredDocumentChannel { Loading = new(true, false) };
        await using var host = await CreateAsync(probe, inbox);
        var person = await host.ContactAsync(Ct, automatic: true);
        var waMessageId = await ReceiveAsync(host, person, probe, "comprobante.pdf", "application/pdf", null);

        await ProcessAsync(host);
        await ProcessAsync(host);

        var message = await MessageAsync(host, waMessageId);
        Assert.Equal((WhatsAppDocumentState.Unavailable, "notAllowed"), (message.DocumentState, message.DocumentFailureCode));
        Assert.Equal(0, probe.Downloads);
        var reply = Assert.Single(await OutgoingAsync(host, person.ContactId));
        Assert.Equal(("Gracias, recibí tu documento. Tu cuenta no puede cargarlo desde acá: se lo pasé a una persona del equipo.", true),
            (reply.Body, reply.IsAssistantHandoff));
        var contact = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().AsNoTracking()
            .SingleAsync(row => row.Id == person.ContactId, Ct));
        Assert.Equal((WhatsAppConversationMode.Manual, "documentNotAllowed", "Mandó un documento y su cuenta no puede cargarlo ni armar formularios."),
            (contact.Mode, contact.HandoffReasonCode, contact.HandoffReason));
        Assert.Empty(inbox.Registrations);
    }

    // Decision 10: someone who builds forms may send the request right after the document. While no form reads documents, the document
    // waits for it, still pending and not downloaded, and the next message ends the wait (pairing it is the circuit's).
    [Fact]
    public async Task A_document_without_a_request_waits_for_the_next_message_and_then_stays_as_today()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var inbox = new CapturingStructuredDocumentChannel { Loading = StructuredDocumentLoading.None };
        await using var host = await CreateAsync(probe, inbox);
        host.Operations.AllowSubmit = true;
        var person = await host.ContactAsync(Ct, automatic: true);
        await GrantAsync(host, person.RecipientId, "formtemplates.read", "formtemplates.manage");
        var waMessageId = await ReceiveAsync(host, person, probe, "comprobante.pdf", "application/pdf", null);

        await ProcessAsync(host);

        var waiting = await MessageAsync(host, waMessageId);
        Assert.Equal((WhatsAppDocumentState.Pending, (DateTime?)waiting.OccurredAtUtc.AddMinutes(10)), (waiting.DocumentState, waiting.DocumentRequestAwaitedUntilUtc));
        Assert.False(await PendingAsync(host, person.ContactId, host.Clock.GetUtcNow().UtcDateTime));
        Assert.False(await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppReceivedDocumentReader>().ExistsPendingAsync(host.Clock.GetUtcNow().UtcDateTime, Ct)));

        await ReceiveTextAsync(host, person, "Armame un formulario con esto");
        await ProcessAsync(host);

        var kept = await MessageAsync(host, waMessageId);
        Assert.Equal((WhatsAppDocumentState.Unavailable, 0), (kept.DocumentState, probe.Downloads));
        Assert.Empty(inbox.Registrations);
    }

    // An audio that follows ends the wait only once transcribed, as a spreadsheet waits for it; a text, at once.
    [Fact]
    public async Task A_document_that_waits_for_its_request_is_due_only_once_its_window_passed_or_a_text_followed()
    {
        await using var host = await CreateAsync(new WhatsAppDocumentProbe(), new CapturingStructuredDocumentChannel());
        var person = await host.ContactAsync(Ct, automatic: true);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        await AddDocumentAsync(host, person.ContactId, now, message => Assert.True(message.AwaitDocumentRequest(now.AddMinutes(10))));

        Assert.False(await PendingAsync(host, person.ContactId, now.AddMinutes(9)));
        Assert.True(await PendingAsync(host, person.ContactId, now.AddMinutes(10)));

        var audio = await FollowAsync(host, person.ContactId, WhatsAppMessageKind.Audio, now.AddMinutes(1), message =>
            Assert.True(message.SetAudioDescriptor("4321", "audio/ogg", new string('d', 64))));
        Assert.False(await PendingAsync(host, person.ContactId, now.AddMinutes(1)));
        var request = await host.InTransactionAsync(async services =>
        {
            var message = await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().SingleAsync(row => row.Id == audio, Ct);
            var requested = message.RequestAudioNormalization(person.RecipientId, "{}", 1, now.AddDays(1), now).Value;
            Assert.True(message.StoreAudio($"whatsapp/audio/{message.Id:N}.ogg", 10).IsSuccess);
            message.SetAudioState(WhatsAppAudioState.Transcribing);
            return requested;
        }, Ct);
        Assert.False(await PendingAsync(host, person.ContactId, now.AddMinutes(1)));
        await host.InTransactionAsync(async services =>
        {
            var message = await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().SingleAsync(row => row.Id == audio, Ct);
            Assert.True(message.CompleteAudio(request, "Cargalo en pagos", now).IsSuccess);
            return true;
        }, Ct);
        Assert.True(await PendingAsync(host, person.ContactId, now.AddMinutes(1)));
    }

    [Fact]
    public async Task A_text_that_follows_ends_the_wait_at_once()
    {
        await using var host = await CreateAsync(new WhatsAppDocumentProbe(), new CapturingStructuredDocumentChannel());
        var person = await host.ContactAsync(Ct, automatic: true);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        await AddDocumentAsync(host, person.ContactId, now, message => Assert.True(message.AwaitDocumentRequest(now.AddMinutes(10))));

        await FollowAsync(host, person.ContactId, WhatsAppMessageKind.Text, now.AddMinutes(1), _ => { });

        Assert.True(await PendingAsync(host, person.ContactId, now.AddMinutes(1)));
    }

    // A slow reading never holds back the reply or the handoff of a document that came after it.
    [Fact]
    public async Task A_pending_document_is_taken_before_a_reading_in_course()
    {
        await using var host = await CreateAsync(new WhatsAppDocumentProbe(), new CapturingStructuredDocumentChannel());
        var person = await host.ContactAsync(Ct, automatic: true);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        await AddDocumentAsync(host, person.ContactId, now.AddMinutes(-5), message =>
        {
            Assert.True(message.StoreDocument($"whatsapp/documents/{message.Id:N}.pdf", 3, new string('b', 64), 1, null).IsSuccess);
            Assert.True(message.RequestDocumentReading(person.RecipientId, "{\"channelId\":\"whatsapp-assistant\"}", 1, now.AddDays(90), now).IsSuccess);
        });
        var pending = await AddDocumentAsync(host, person.ContactId, now, _ => { });

        var found = await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppReceivedDocumentReader>().FindPendingAsync(person.ContactId, now, Ct));

        Assert.Equal((pending, WhatsAppDocumentState.Pending), (found!.MessageId, found.State));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Anyone_else_keeps_it_as_today_without_downloading_it(bool automatic, bool formsRead)
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var inbox = new CapturingStructuredDocumentChannel { Loading = formsRead ? new(true, true) : StructuredDocumentLoading.None };
        await using var host = await CreateAsync(probe, inbox);
        var person = await host.ContactAsync(Ct, automatic);
        var waMessageId = await ReceiveAsync(host, person, probe, "comprobante.pdf", "application/pdf", null);

        await ProcessAsync(host);

        var message = await MessageAsync(host, waMessageId);
        Assert.Equal((WhatsAppMessageKind.ReceivedDocument, WhatsAppDocumentState.Unavailable, "comprobante.pdf"),
            (message.Kind, message.DocumentState, message.DocumentFileName));
        Assert.Equal(0, probe.Downloads);
        Assert.Empty(await OutgoingAsync(host, person.ContactId));
    }

    // The circuit says it (task 9); here the day's quota leaves the document registered unclassified, from its copy, and not read.
    [Fact]
    public async Task Over_the_day_reading_quota_the_document_goes_to_the_inbox_unclassified()
    {
        var probe = new WhatsAppDocumentProbe { Bytes = Pdf() };
        var inbox = new CapturingStructuredDocumentChannel();
        await using var host = await CreateAsync(probe, inbox);
        host.Operations.AllowSubmit = true;
        host.Operations.SubmissionFailure = Error.TooManyRequests("Ai.Assistant.QuotaExceeded", "Fictitious quota.", new Dictionary<string, object?> { ["retryAfter"] = 60 });
        var person = await host.ContactAsync(Ct, automatic: true);
        var waMessageId = await ReceiveAsync(host, person, probe, "comprobante.pdf", "application/pdf", null);

        await ProcessAsync(host);

        var message = await MessageAsync(host, waMessageId);
        Assert.Equal((WhatsAppDocumentState.Unavailable, "quota"), (message.DocumentState, message.DocumentFailureCode));
        var registered = Assert.Single(inbox.Registrations);
        Assert.Equal((StructuredDocumentNotRead.Quota, message.DocumentStorageKey, (int?)1), (registered.NotRead, registered.Copy!.StorageKey, registered.Copy.Pages));
        Assert.Empty(await OutgoingAsync(host, person.ContactId));
    }

    [Fact]
    public async Task A_failed_download_waits_its_turn_and_a_reading_in_course_is_polled_in_every_round()
    {
        await using var host = await CreateAsync(new WhatsAppDocumentProbe(), new CapturingStructuredDocumentChannel());
        var person = await host.ContactAsync(Ct, automatic: true);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var failed = await AddDocumentAsync(host, person.ContactId, now, message => message.RegisterDocumentFailure(now, "Files.Content.Unavailable"));

        Assert.False(await PendingAsync(host, person.ContactId, now));
        Assert.True(await PendingAsync(host, person.ContactId, now.AddSeconds(30)));
        Assert.True(await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppReceivedDocumentReader>().ExistsPendingAsync(now, Ct)));

        await host.InTransactionAsync(async services =>
        {
            (await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().SingleAsync(row => row.Id == failed, Ct))
                .SetDocumentUnavailable("Files.Content.Unavailable");
            return true;
        }, Ct);
        Assert.False(await PendingAsync(host, person.ContactId, now.AddMinutes(1)));

        var reading = await AddDocumentAsync(host, person.ContactId, now, message =>
        {
            Assert.True(message.StoreDocument($"whatsapp/documents/{message.Id:N}.pdf", 3, new string('b', 64), 1, null).IsSuccess);
            Assert.True(message.RequestDocumentReading(person.RecipientId, "{\"channelId\":\"whatsapp-assistant\"}", 1, now.AddDays(90), now).IsSuccess);
        });
        var found = await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppReceivedDocumentReader>().FindPendingAsync(person.ContactId, now, Ct));
        Assert.Equal((reading, WhatsAppDocumentState.Reading), (found!.MessageId, found.State));
        Assert.Equal(reading, (await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppReceivedDocumentReader>()
            .FindByRequestAsync(person.ContactId, found.RequestId!.Value, Ct)))!.MessageId);
        Assert.False(await PendingAsync(host, person.ContactId, now.AddDays(91)));
    }

    [Fact]
    public async Task Retention_takes_the_descriptor_the_copy_and_the_reading_with_the_body_and_keeps_only_live_copies()
    {
        await using var host = await CreateAsync(new WhatsAppDocumentProbe(), new CapturingStructuredDocumentChannel());
        var person = await host.ContactAsync(Ct, automatic: true);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var old = now.AddDays(-91);
        var stored = await AddDocumentAsync(host, person.ContactId, now, message => Read(message, now));
        var pending = await AddDocumentAsync(host, person.ContactId, now, _ => { });
        var rejected = await AddDocumentAsync(host, person.ContactId, now, message => message.RejectDocument("protected"));
        var expired = await AddDocumentAsync(host, person.ContactId, old, message => Read(message, old));
        string[] keys = [$"whatsapp/documents/{stored:N}.pdf", $"whatsapp/documents/{pending:N}.jpg", $"whatsapp/documents/{rejected:N}.pdf",
            $"whatsapp/documents/{expired:N}.pdf", $"whatsapp/documents/{stored:N}.csv"];

        var referenced = await host.ScopeAsync(services => services.GetServices<IPrivateFileRetentionSource>()
            .OfType<WhatsAppReceivedDocumentRetentionSource>().Single().ListReferencedKeysAsync(keys, Ct));
        Assert.Equal(keys[..2].Order(StringComparer.Ordinal), referenced.Order(StringComparer.Ordinal));

        await host.Host.Services.GetRequiredService<WhatsAppMessageRetentionService>().ClearExpiredTextsAsync(Ct);

        var cleared = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .SingleAsync(row => row.Id == expired, Ct));
        Assert.All(new[] { cleared.Body, cleared.DocumentMediaId, cleared.DocumentContentType, cleared.DocumentSha256, cleared.DocumentFileName,
            cleared.DocumentStorageKey, cleared.DocumentCopySha256, cleared.DocumentReadingJson, cleared.DocumentContextJson }, value => Assert.Null(value));
        Assert.Equal(((long?)null, (int?)null, WhatsAppDocumentState.Read), (cleared.DocumentSize, cleared.DocumentPages, cleared.DocumentState));
        var live = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .SingleAsync(row => row.Id == stored, Ct));
        Assert.Equal(("Cargalo en pagos", "comprobante.pdf"), (live.Body, live.DocumentFileName));
        Assert.NotNull(live.DocumentReadingJson);

        static void Read(WhatsAppMessage message, DateTime at)
        {
            Assert.True(message.StoreDocument($"whatsapp/documents/{message.Id:N}.pdf", 3, new string('b', 64), 1, null).IsSuccess);
            var request = message.RequestDocumentReading(Guid.NewGuid(), "{\"channelId\":\"whatsapp-assistant\"}", 1, at.AddDays(90), at).Value;
            Assert.True(message.CompleteDocumentReading(request, "{\"readability\":\"readable\",\"text\":\"Total\",\"truncated\":false}", at).IsSuccess);
        }
    }

    private Task<WhatsAppAssistantScenario> CreateAsync(WhatsAppDocumentProbe probe, CapturingStructuredDocumentChannel inbox) =>
        WhatsAppAssistantScenario.CreateAsync(factory, Ct, builder => builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IPrivateFileStore>(probe));
            services.Replace(ServiceDescriptor.Singleton<IWhatsAppMediaClient>(probe));
            services.Replace(ServiceDescriptor.Singleton<IStructuredDocumentChannel>(inbox));
        }));

    /// <summary>A one-page PDF with a title of its own, so no two documents repeat each other.</summary>
    private static byte[] Pdf()
    {
        using var output = new MemoryStream();
        using (var pdf = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata { Title = Guid.NewGuid().ToString("N") }))
        {
            using var paint = new SKPaint { Color = SKColors.Black };
            pdf.BeginPage(200, 200).DrawRect(10, 10, 40, 20, paint);
            pdf.EndPage();
            pdf.Close();
        }
        return output.ToArray();
    }

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

    private static async Task ReceiveTextAsync(WhatsAppAssistantScenario host, WhatsAppAssistantContact person, string text)
    {
        var contact = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().AsNoTracking()
            .SingleAsync(row => row.Id == person.ContactId, Ct));
        var body = MetaWebhook.Build(contacts: [MetaWebhook.Contact(contact.WaId, contact.UserIdentifier, contact.ProfileName)],
            messages: [MetaWebhook.Text(MetaWebhook.UniqueWaMessageId(), contact.WaId, contact.UserIdentifier, host.Clock.GetUtcNow().UtcDateTime, text)]);
        using var client = host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // A role of its own with the permissions, besides completing forms, as an administrator would give it.
    private static Task<bool> GrantAsync(WhatsAppAssistantScenario host, Guid userId, params string[] permissions) => host.InTransactionAsync(async services =>
    {
        var role = "document-sender-" + Guid.NewGuid().ToString("N");
        await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, [.. permissions, "formrecords.create"], Ct);
        var users = services.GetRequiredService<IUserRepository>();
        await users.SetRolesAsync(userId, [role], Ct);
        return true;
    }, Ct);

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
        var reader = services.GetRequiredService<IWhatsAppReceivedDocumentReader>();
        var found = await reader.FindPendingAsync(contactId, nowUtc, Ct) is not null;
        Assert.Equal(found, (await reader.ListPendingContactsAsync([], 50, nowUtc, Ct)).Contains(contactId));
        return found;
    });

    // Another message of the person, after the ones before it.
    private static Task<Guid> FollowAsync(WhatsAppAssistantScenario host, Guid contactId, WhatsAppMessageKind kind, DateTime occurredAtUtc,
        Action<WhatsAppMessage> arrange) => host.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var contact = await db.Set<WhatsAppContact>().SingleAsync(row => row.Id == contactId, Ct);
            var message = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), kind, kind == WhatsAppMessageKind.Text ? "Cargalo" : null, null,
                occurredAtUtc);
            message.Classify(WhatsAppMessagePurpose.Business);
            message.SetInboundSequence(contact.RegisterInboundMessage());
            arrange(message);
            db.Add(message);
            return message.Id;
        }, Ct);

    private static Task<Guid> AddDocumentAsync(WhatsAppAssistantScenario host, Guid contactId, DateTime occurredAtUtc, Action<WhatsAppMessage> arrange) =>
        host.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var contact = await db.Set<WhatsAppContact>().SingleAsync(row => row.Id == contactId, Ct);
            var message = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.ReceivedDocument, "Cargalo en pagos", null, occurredAtUtc);
            Assert.True(message.SetDocumentDescriptor("1234", "application/pdf", new string('a', 64), "comprobante.pdf"));
            message.Classify(WhatsAppMessagePurpose.Business);
            message.SetInboundSequence(contact.RegisterInboundMessage());
            message.MarkProcessed(occurredAtUtc);
            arrange(message);
            db.Add(message);
            return message.Id;
        }, Ct);
}
