using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed partial class WhatsAppFlowReplyServiceTests
{
    [Fact]
    public async Task A_reply_is_decoded_with_its_sealed_mapping_before_business_forms_capture()
    {
        var fixture = new Fixture();
        Assert.Equal("flow-static-7.3-r3", fixture.Projection.ProfileRevision);
        Assert.NotEqual(WhatsAppFlowCompiler.ProfileRevision, fixture.Projection.ProfileRevision);
        var sealedProjection = fixture.Projection;
        var answers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [fixture.Mapping[0].Key] = "texto",
            [fixture.Mapping[1].Key] = "texto largo",
            [fixture.Mapping[2].Key] = "1,25",
            [fixture.Mapping[3].Key] = "2025-02-03",
            [fixture.Mapping[4].Key] = "false",
            [fixture.Mapping[5].Key] = fixture.OptionId.ToString("D"),
        };
        var message = fixture.AddReply("wamid.valid", answers);

        await fixture.Service.ProcessPendingAsync(CancellationToken.None);

        var captured = Assert.IsType<StructuredRecordCaptureRequest>(fixture.Sink.Request);
        Assert.Equal(fixture.Session.Id, captured.SourceReceiptId);
        Assert.Equal(fixture.Session.Culture, captured.PresentationCulture);
        Assert.Equal("1.25", captured.Answers[fixture.Mapping[2].Key]);
        Assert.Equal("2025-02-03", captured.Answers[fixture.Mapping[3].Key]);
        Assert.Equal("false", captured.Answers[fixture.Mapping[4].Key]);
        Assert.Equal(fixture.OptionId.ToString("D"), captured.Answers[fixture.Mapping[5].Key]);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, message.FlowCaptureState);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, fixture.Session.CaptureState);
        Assert.Equal(sealedProjection, fixture.Projection);
    }

    [Fact]
    public async Task A_conflicting_second_message_is_rejected_without_relabeling_the_first_completed_record()
    {
        var fixture = new Fixture();
        var answers = fixture.ValidAnswers("primero");
        var first = fixture.AddReply("wamid.first", answers);
        var second = fixture.AddReply("wamid.second", fixture.ValidAnswers("distinto"));
        fixture.Sink.Results.Add(new("Completed", Guid.NewGuid(), 1, null));
        fixture.Sink.Results.Add(new("Rejected", null, null, "Forms.Records.ReplayConflict"));

        await fixture.Service.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, first.FlowCaptureState);
        Assert.Equal(WhatsAppFlowCaptureState.Rejected, second.FlowCaptureState);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, fixture.Session.CaptureState);
        Assert.Equal("VIS-000001", fixture.Session.CompletedNumber);
    }

    [Fact]
    public async Task A_decimal_rejected_by_the_profile_codec_is_never_forwarded_as_a_valid_decimal()
    {
        var fixture = new Fixture();
        var message = fixture.AddReply("wamid.invalid-decimal", new Dictionary<string, string>
        {
            [fixture.Mapping[2].Key] = "+1",
        });

        await fixture.Service.ProcessPendingAsync(CancellationToken.None);

        Assert.Null(fixture.Sink.Request);
        Assert.Equal(0, fixture.Sink.CaptureCount);
        Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
    }

    [Fact]
    public async Task A_text_value_with_a_null_character_is_rejected_without_blocking_the_next_reply()
    {
        var fixture = new Fixture();
        var invalid = fixture.AddReply("wamid.null", fixture.ValidAnswers("antes\0despues"));
        var valid = fixture.AddReply("wamid.valid-after-null", fixture.ValidAnswers("válido"));

        await fixture.Service.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(WhatsAppFlowCaptureState.Rejected, invalid.FlowCaptureState);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, valid.FlowCaptureState);
        Assert.Equal(1, fixture.Sink.CaptureCount);
        Assert.Equal("válido", fixture.Sink.Request!.Answers[fixture.Mapping[0].Key]);
    }

    [Fact]
    public async Task A_malformed_surrogate_is_rejected_without_blocking_the_next_reply()
    {
        var fixture = new Fixture();
        var key = fixture.Mapping[0].Key;
        var invalid = fixture.AddReplyJson("wamid.surrogate", "{\"flow_token\":\"secret-flow-token\",\"" + key + "\":\"\\ud800\"}");
        var valid = fixture.AddReply("wamid.valid-after-surrogate", fixture.ValidAnswers("válido"));

        await fixture.Service.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(WhatsAppFlowCaptureState.Rejected, invalid.FlowCaptureState);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, valid.FlowCaptureState);
        Assert.Equal(1, fixture.Sink.CaptureCount);
    }

    [Fact]
    public async Task An_oversized_response_without_a_token_is_persisted_as_rejected_without_answers()
    {
        var fixture = new Fixture();
        var reply = fixture.AddReplyJson("wamid.oversized", "{\"_flow_error\":\"ResponseTooLarge\"}");

        await fixture.Service.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(WhatsAppFlowCaptureState.Rejected, reply.FlowCaptureState);
        Assert.Null(reply.FlowRecordId);
        Assert.Null(reply.FlowRecordNumber);
        Assert.Equal(0, fixture.Sink.CaptureCount);
    }

    [Fact]
    public async Task An_inactive_or_relinked_recipient_can_recover_only_an_exact_prior_receipt()
    {
        var fixture = new Fixture();
        fixture.Sessions.IsCurrentActiveRecipient = false;
        var reply = fixture.AddReply("wamid.recover-receipt", fixture.ValidAnswers("answer"));
        fixture.Sink.ExistingReceipt = new("Completed", Guid.NewGuid(), 24, null);

        await fixture.Service.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, reply.FlowCaptureState);
        Assert.NotNull(reply.FlowRecordId);
        Assert.Equal("VIS-000024", reply.FlowRecordNumber);
        Assert.Equal(0, fixture.Sink.CaptureCount);
        Assert.Equal(1, fixture.Sink.ReceiptProbeCount);
    }

    [Fact]
    public async Task An_inactive_or_relinked_recipient_without_a_prior_receipt_cannot_create_a_record()
    {
        var fixture = new Fixture();
        fixture.Sessions.IsCurrentActiveRecipient = false;
        var reply = fixture.AddReply("wamid.no-receipt", fixture.ValidAnswers("answer"));

        await fixture.Service.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(WhatsAppFlowCaptureState.Unrecognized, reply.FlowCaptureState);
        Assert.Null(reply.FlowRecordId);
        Assert.Equal(0, fixture.Sink.CaptureCount);
        Assert.Equal(1, fixture.Sink.ReceiptProbeCount);
    }

    [Fact]
    public async Task A_reply_submitted_before_expiry_is_accepted_when_the_worker_processes_it_late()
    {
        var fixture = new Fixture();
        var reply = fixture.AddReply("wamid.late", fixture.ValidAnswers("válido"));
        fixture.Clock.Advance(TimeSpan.FromHours(25));

        await fixture.Service.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, reply.FlowCaptureState);
        Assert.Equal(1, fixture.Sink.CaptureCount);
    }

    [Fact]
    public async Task A_reply_submitted_at_expiry_is_rejected_even_when_the_worker_runs_before_expiry()
    {
        var fixture = new Fixture();
        var reply = fixture.AddReply("wamid.expired", fixture.ValidAnswers("tarde"), fixture.Session.ExpiresAtUtc);

        await fixture.Service.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(WhatsAppFlowCaptureState.Unrecognized, reply.FlowCaptureState);
        Assert.Equal(0, fixture.Sink.CaptureCount);
    }

    [Fact]
    public async Task A_failure_processing_one_reply_does_not_block_later_replies()
    {
        var fixture = new Fixture();
        var failed = fixture.AddReply("wamid.retry", fixture.ValidAnswers("primero"));
        var next = fixture.AddReply("wamid.next", fixture.ValidAnswers("segundo"));
        fixture.Sink.ThrowOnNextCapture = true;

        await fixture.Service.ProcessPendingAsync(CancellationToken.None);

        Assert.Null(failed.FlowCaptureState);
        Assert.Null(failed.ProcessedAtUtc);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, next.FlowCaptureState);
        Assert.Equal(2, fixture.Sink.CaptureCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_permanently_failing_reply_is_rejected_after_five_attempts_and_fifteen_minutes(bool unavailable)
    {
        var fixture = new Fixture();
        var reply = fixture.AddReply("wamid.permanent", fixture.ValidAnswers("answer"));
        fixture.Sink.AlwaysThrow = !unavailable;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            if (unavailable) fixture.Sink.Results.Add(new("Unavailable", null, null, null));
            await fixture.Service.ProcessPendingAsync(CancellationToken.None);
            if (attempt < 5) Assert.Null(reply.ProcessedAtUtc);
            fixture.Clock.Advance(TimeSpan.FromMinutes(4));
        }
        Assert.Equal(WhatsAppFlowCaptureState.Rejected, reply.FlowCaptureState);
        Assert.NotNull(reply.ProcessedAtUtc);
        Assert.Null(reply.FlowRecordId);
        Assert.Null(reply.FlowRecordNumber);
        Assert.Equal(fixture.Session.Id, reply.FlowSessionId);
        Assert.Equal(WhatsAppFlowCaptureState.Rejected, fixture.Session.CaptureState);
        await fixture.Service.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(5, fixture.Sink.CaptureCount);
    }

    [Fact]
    public async Task A_three_minute_infrastructure_outage_recovers_without_rejecting_or_duplicating_the_reply()
    {
        var fixture = new Fixture();
        var reply = fixture.AddReply("wamid.short-outage", fixture.ValidAnswers("answer"));
        fixture.Sink.AlwaysThrow = true;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await fixture.Service.ProcessPendingAsync(CancellationToken.None);
            fixture.Clock.Advance(TimeSpan.FromSeconds(30));
            Assert.Null(reply.ProcessedAtUtc);
            Assert.Null(reply.FlowCaptureState);
        }
        fixture.Sink.AlwaysThrow = false;
        await fixture.Service.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, reply.FlowCaptureState);
        Assert.Equal(7, fixture.Sink.CaptureCount);
        await fixture.Service.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(7, fixture.Sink.CaptureCount);
    }

    [Fact]
    public async Task A_transient_capture_failure_is_retried_and_can_complete()
    {
        var fixture = new Fixture();
        var reply = fixture.AddReply("wamid.transient", fixture.ValidAnswers("answer"));
        fixture.Sink.ThrowOnNextCapture = true;
        await fixture.Service.ProcessPendingAsync(CancellationToken.None);
        Assert.Null(reply.ProcessedAtUtc);
        await fixture.Service.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, reply.FlowCaptureState);
        Assert.Equal(2, fixture.Sink.CaptureCount);
    }

    [Fact]
    public async Task A_dynamic_choice_is_decoded_only_from_the_protected_session_offer()
    {
        var fixture = new Fixture(catalog: true);
        var message = fixture.AddReply("wamid.catalog", fixture.ValidAnswers("answer"));
        await fixture.Service.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, message.FlowCaptureState);
        var offer = Assert.Single(fixture.Sink.Request!.Catalogs!);
        Assert.Equal(fixture.OptionId, Assert.Single(offer.Options).Id);
        Assert.Equal("Shown text", offer.Options[0].Text);
    }

    [Fact]
    public async Task A_dynamic_choice_outside_the_offer_never_reaches_the_sink()
    {
        var fixture = new Fixture(catalog: true);
        var answers = fixture.ValidAnswers("answer");
        answers[fixture.Mapping[5].Key] = Guid.NewGuid().ToString("D");
        var message = fixture.AddReply("wamid.not-offered", answers);
        await fixture.Service.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
        Assert.Null(fixture.Sink.Request);
    }

    [Fact]
    public async Task An_inactive_choice_notice_names_only_the_question()
    {
        var fixture = new Fixture(catalog: true);
        fixture.Sink.Results.Add(new("Rejected", null, null, "Forms.Record.CatalogItemInactive", fixture.Mapping[5].FieldId));
        var message = fixture.AddReply("wamid.inactive", fixture.ValidAnswers("answer"));
        await fixture.Service.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal("Persona", message.FlowRejectedQuestion);
        Assert.Equal("Persona", fixture.Session.RejectedQuestion);
        Assert.Null(message.FlowRecordNumber);
        Assert.DoesNotContain("Shown text", message.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_media_reply_downloads_before_calling_the_record_sink_and_keeps_file_metadata_out_of_answers()
    {
        var fixture = new Fixture(media: true);
        var message = fixture.AddFileReply("wamid.media");
        await fixture.Service.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, message.FlowCaptureState);
        Assert.Equal(1, fixture.Media.DownloadCount);
        Assert.Equal(1, message.FlowFileCount);
        var request = Assert.IsType<StructuredRecordCaptureRequest>(fixture.Sink.Request);
        Assert.Empty(request.Answers);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(request.Files!).Content);
    }

    [Fact]
    public async Task An_unavailable_media_file_retries_then_rejects_without_any_call_to_the_sink()
    {
        var fixture = new Fixture(media: true);
        fixture.Media.Result = ArquitecturaBase.Domain.Files.FileErrors.Unavailable;
        var message = fixture.AddFileReply("wamid.media.failed");
        await fixture.Service.ProcessPendingAsync(CancellationToken.None);
        Assert.Null(message.ProcessedAtUtc);
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        for (var index = 0; index < 4; index++) await fixture.Service.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
        Assert.Equal(ArquitecturaBase.Domain.Files.FileErrors.Unavailable.Code, message.FlowCaptureErrorCode);
        Assert.Equal(0, fixture.Sink.CaptureCount);
    }

    [Fact]
    public async Task A_reply_deadline_releases_the_batch_and_the_next_reply_can_complete()
    {
        var fixture = new Fixture(media: true);
        var slow = fixture.AddFileReply("wamid.slow");
        var next = fixture.AddFileReply("wamid.after-slow");
        fixture.Media.OnDownload = _ =>
        {
            if (fixture.Media.DownloadCount == 1) fixture.Clock.Advance(TimeSpan.FromSeconds(91));
        };
        await fixture.Service.ProcessPendingAsync(CancellationToken.None);
        Assert.Null(slow.ProcessedAtUtc);
        Assert.Equal(1, slow.FlowReplyFailureCount);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, next.FlowCaptureState);
        Assert.Equal(1, fixture.Sink.CaptureCount);
    }

    [Fact]
    public async Task File_rejections_name_the_question_from_the_sealed_flow_without_file_values()
    {
        var fixture = new Fixture(media: true);
        fixture.Media.Result = ArquitecturaBase.Domain.Files.FileErrors.HashMismatch;
        var message = fixture.AddFileReply("wamid.invalid-file");
        await fixture.Service.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
        Assert.Equal("Foto de prueba", message.FlowRejectedQuestion);
        Assert.Null(message.FlowRecordNumber);
    }

    private sealed class Fixture
    {
        private static readonly DateTime Now = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        private readonly string _token = "secret-flow-token";
        private readonly Guid _contactId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Guid _actorId = Guid.NewGuid();
        private readonly Guid _definitionId = Guid.NewGuid();
        private readonly Guid _versionId = Guid.NewGuid();
        private readonly Guid _bindingId = Guid.NewGuid();
        private readonly Guid _operationId = Guid.NewGuid();
        private readonly LockLog _locks = new();
        private readonly Dictionary<string, string> _responses = new(StringComparer.Ordinal);

        public Fixture(bool catalog = false, bool media = false, bool assisted = false, bool automatic = false)
        {
            var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_token)));
            Session = automatic
                ? WhatsAppFlowSession.CreateAssistant(_contactId, 0, _userId, _definitionId, _versionId,
                    2, _bindingId, "es", "{\"es\":\"Formulario\"}", "VIS", tokenHash, new string('a', 64), Now.AddMinutes(20))
                : WhatsAppFlowSession.Create(_contactId, 0, _userId, _actorId, _definitionId, _versionId,
                    2, _bindingId, "es", "{\"es\":\"Formulario\"}", "VIS", tokenHash, new string('a', 64), Now.AddMinutes(20));
            Session.SealPayload([1]);
            Mapping =
            [
                Field(0, "shortText", 40),
                Field(1, "longText", 300),
                Field(2, "decimal"),
                Field(3, "date"),
                Field(4, "boolean"),
                Field(5, "choice", choices: catalog ? [] : [OptionId]),
            ];
            if (media) Mapping = [Field(0, "photo") with { Files = new(1, 2, "camera") }];
            Projection = new(1, media ? JsonSerializer.Serialize(new { screens = new[] { new { layout = new { children = new[] { new { name = Mapping[0].Key, label = "Foto de prueba" } } } } } }) : "{}", new string('b', 64), "flow-static-7.3-r3", 1, "es", Mapping);
            var replySession = new WhatsAppFlowReplySessionRow(Session.Id, _contactId, _userId, _definitionId,
                _versionId, _bindingId, _operationId, "es", [2], "VIS", tokenHash, [1], Now.AddMinutes(20), true,
                WhatsAppFlowCaptureState.Pending, null, Session.OperatorUserId)
            { Origin = Session.Origin };
            Messages = new InMemoryWhatsAppMessageRepository(_locks);
            Sessions = new FakeSessionReader(replySession, Messages, Session);
            var sessionRepository = new FakeSessionRepository(Session);
            Sink = new FakeSink();
            Clock = new FakeTimeProvider(Now);
            var stateService = new WhatsAppFlowReplyStateService(Messages, sessionRepository, new FakeUnitOfWork(), Clock);
            Media = new FakeWhatsAppMediaClient();
            SessionProtector = new FakeSessionProtector(new(automatic ? 2 : 1, _contactId, _userId, Session.OperatorUserId, _definitionId, _versionId, _bindingId,
                "es", new string('c', 64), 0, "1", "flow-name", Projection.Hash, _token, "Abrir", "START",
                new("waba", "phone", "v25.0", new string('d', 64)), catalog ? [new StructuredFormCatalogOffer(Mapping[5].FieldId, Mapping[5].Key, "Persona", Guid.NewGuid(), 2,
                    "Manual", "Personas", [new(OptionId, "Shown text", "Detail")])] : [], Session.Origin));
            if (assisted)
            {
                var origin = new StructuredRecordCaptureOrigin(automatic ? StructuredRecordSenderKind.Assistant : StructuredRecordSenderKind.Manual,
                    Session.OperatorUserId, "A captured reason", new(Guid.NewGuid(), "forms.propose-registration", 1,
                        new("qualified", 1, "instructions", 1, Now, automatic ? "whatsapp-assistant" : "whatsapp-manual")),
                    [new(Mapping[0].FieldId, "shortText", "proposed")], [Mapping[0].FieldId]);
                SessionProtector.Content = new(1, origin, [new(Mapping[0].FieldId, "Pregunta", "proposed")]);
                Assert.True(Session.SealAssistantContent([3], Now.AddDays(90)));
            }
            ProjectionProtector = new FakeProjectionProtector(Projection);
            Service = new WhatsAppFlowReplyService(Sessions, new FakeReplyProtector(_responses), new WhatsAppFlowSessionVerifier(SessionProtector, Clock),
                ProjectionProtector, Sink, stateService,
                NullLogger<WhatsAppFlowReplyService>.Instance, new WhatsAppFlowMediaRecorder(Media, Clock));
        }

        public Guid OptionId { get; } = Guid.NewGuid();
        public IReadOnlyList<WhatsAppFlowProjectionField> Mapping { get; }
        public WhatsAppFlowProjection Projection { get; }
        public WhatsAppFlowSession Session { get; }
        public InMemoryWhatsAppMessageRepository Messages { get; }
        public FakeSink Sink { get; }
        public FakeWhatsAppMediaClient Media { get; }
        public FakeSessionReader Sessions { get; }
        public FakeTimeProvider Clock { get; }
        public WhatsAppFlowReplyService Service { get; }
        public FakeSessionProtector SessionProtector { get; }
        public FakeProjectionProtector ProjectionProtector { get; }

        public WhatsAppMessage AddFileReply(string id) => AddReplyJson(id, JsonSerializer.Serialize(new Dictionary<string, object>
        { ["flow_token"] = _token, [Mapping[0].Key] = new[] { new { id = "123", file_name = "photo.png", mime_type = "image/png", sha256 = new string('a', 64) } } }));

        public WhatsAppMessage AddReply(string waMessageId, IReadOnlyDictionary<string, string> answers, DateTime? occurredAtUtc = null)
        {
            var json = JsonSerializer.Serialize(new Dictionary<string, string>(answers, StringComparer.Ordinal)
            {
                ["flow_token"] = _token,
            });
            return AddReplyJson(waMessageId, json, occurredAtUtc);
        }

        public WhatsAppMessage AddReplyJson(string waMessageId, string json, DateTime? occurredAtUtc = null)
        {
            _responses.Add(waMessageId, json);
            var message = WhatsAppMessage.Inbound(_contactId, waMessageId, WhatsAppMessageKind.FlowReply,
                null, null, occurredAtUtc ?? Now, [1]);
            Messages.Messages.Add(message);
            return message;
        }

        public Dictionary<string, string> ValidAnswers(string shortText) => new(StringComparer.Ordinal)
        {
            [Mapping[0].Key] = shortText,
            [Mapping[1].Key] = "long text",
            [Mapping[2].Key] = "1.25",
            [Mapping[3].Key] = "2025-02-03",
            [Mapping[4].Key] = "false",
            [Mapping[5].Key] = OptionId.ToString("D"),
        };

        private static string Key(int index) => "field_" + Guid.Parse("00000000-0000-7000-8000-00000000000" + index.ToString(CultureInfo.InvariantCulture)).ToString("N");
        private static WhatsAppFlowProjectionField Field(int index, string type, int? maxLength = null,
            IReadOnlyList<Guid>? choices = null)
        {
            var key = Key(index);
            return new(Guid.Parse(key[6..]), key, type, true, maxLength, choices ?? []);
        }
    }

    private sealed class FakeSessionReader(WhatsAppFlowReplySessionRow session,
        InMemoryWhatsAppMessageRepository messages, WhatsAppFlowSession actualSession) : IWhatsAppFlowSessionReader
    {
        public bool IsCurrentActiveRecipient { get; set; } = true;
        public WhatsAppFlowReplySessionRow Row { get; set; } = session;
        public Task<IReadOnlyList<WhatsAppPendingFlowReplyRow>> ListPendingFlowRepliesAsync(int limit, CancellationToken cancellationToken) =>
            messages.ListPendingFlowRepliesAsync(limit, cancellationToken);
        public Task<WhatsAppFlowReplySessionRow?> FindReplySessionAsync(Guid contactId, string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult<WhatsAppFlowReplySessionRow?>(contactId == Row.ContactId && tokenHash == Row.TokenHash
                ? Row with { IsCurrentActiveRecipient = IsCurrentActiveRecipient,
                    ProtectedAssistantContent = actualSession.ProtectedAssistantContent, AssistantContentExpiresAtUtc = actualSession.AssistantContentExpiresAtUtc } : null);
        public Task<WhatsAppFormBindingRow?> FindBindingAsync(Guid definitionId, Guid versionId, string culture, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<WhatsAppFormDispatchRow?> FindDispatchAsync(Guid outgoingId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSessionRepository(WhatsAppFlowSession session) : IWhatsAppFlowSessionRepository
    {
        public Task<WhatsAppFlowSession?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<WhatsAppFlowSession?>(id == session.Id ? session : null);
        public void Add(WhatsAppFlowSession value) => throw new NotSupportedException();
    }

    private sealed class FakeReplyProtector(IReadOnlyDictionary<string, string> responses) : IWhatsAppFlowReplyProtector
    {
        public byte[] Protect(string waMessageId, Guid contactId, string responseJson) => throw new NotSupportedException();
        public string Read(string waMessageId, Guid contactId, byte[] protectedResponse) => responses[waMessageId];
    }

    private sealed class FakeSessionProtector(WhatsAppFlowSessionPayload payload) : IWhatsAppFlowSessionProtector
    {
        public WhatsAppFlowSessionPayload Payload { get; set; } = payload;
        public WhatsAppFlowAssistantContent? Content { get; set; }
        public int AssistantReadCount { get; private set; }
        public bool FailAssistantRead { get; set; }
        public byte[] Protect(Guid sessionId, Guid contactId, Guid userId, Guid versionId, WhatsAppFlowSessionPayload value) => throw new NotSupportedException();
        public byte[] ProtectAssistant(Guid sessionId, Guid contactId, Guid userId, Guid versionId, WhatsAppFlowAssistantContent content) => throw new NotSupportedException();
        public WhatsAppFlowAssistantContent ReadAssistant(Guid sessionId, Guid contactId, Guid userId, Guid versionId, byte[] content)
        {
            AssistantReadCount++;
            if (FailAssistantRead || Content is null) throw new CryptographicException("The assistant purpose does not match.");
            return Content;
        }
        public WhatsAppFlowSessionPayload Read(Guid sessionId, Guid contactId, Guid userId, Guid versionId, byte[] value) => Payload;
    }

    private sealed class FakeProjectionProtector(WhatsAppFlowProjection projection) : IWhatsAppFlowProtector
    {
        public WhatsAppFlowProjection Projection { get; set; } = projection;
        public byte[] ProtectManifest(Guid operationId, Guid neutralOperationId, Guid versionId, WhatsAppFlowStoredManifest manifest) => throw new NotSupportedException();
        public WhatsAppFlowStoredManifest ReadManifest(Guid operationId, Guid neutralOperationId, Guid versionId, byte[] protectedManifest) => throw new NotSupportedException();
        public byte[] ProtectProjection(Guid operationId, Guid bindingId, Guid versionId, string culture, WhatsAppFlowProjection value) => throw new NotSupportedException();
        public WhatsAppFlowProjection ReadProjection(Guid operationId, Guid bindingId, Guid versionId, string culture, byte[] protectedProjection) => Projection;
    }

    private sealed class FakeSink : IStructuredRecordSink
    {
        public StructuredRecordCaptureRequest? Request { get; private set; }
        public StructuredRecordCaptureRequest? ReceiptRequest { get; private set; }
        public int CaptureCount { get; private set; }
        public int ReceiptProbeCount { get; private set; }
        public StructuredRecordCaptureResponse? ExistingReceipt { get; set; }
        public bool ThrowOnNextCapture { get; set; }
        public bool AlwaysThrow { get; set; }
        public List<StructuredRecordCaptureResponse> Results { get; } = [];
        public Task<StructuredRecordCaptureResponse> CaptureAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken)
        {
            CaptureCount++;
            if (ThrowOnNextCapture || AlwaysThrow)
            {
                ThrowOnNextCapture = false;
                throw new InvalidOperationException("capture failed");
            }
            Request = request;
            var result = Results.Count == 0 ? new StructuredRecordCaptureResponse("Completed", Guid.NewGuid(), 24, null) : Results[0];
            if (Results.Count != 0) Results.RemoveAt(0);
            return Task.FromResult(result);
        }

        public Task<StructuredRecordCaptureResponse?> FindExistingReceiptAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken)
        {
            ReceiptProbeCount++; ReceiptRequest = request;
            return Task.FromResult(ExistingReceipt);
        }
    }
}
