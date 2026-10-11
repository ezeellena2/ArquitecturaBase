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
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppFlowReplyServiceTests
{
    [Fact]
    public async Task A_reply_is_decoded_with_its_sealed_mapping_before_business_forms_capture()
    {
        var fixture = new Fixture();
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
        Assert.Equal("1.25", captured.Answers[fixture.Mapping[2].Key]);
        Assert.Equal("2025-02-03", captured.Answers[fixture.Mapping[3].Key]);
        Assert.Equal("false", captured.Answers[fixture.Mapping[4].Key]);
        Assert.Equal(fixture.OptionId.ToString("D"), captured.Answers[fixture.Mapping[5].Key]);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, message.FlowCaptureState);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, fixture.Session.CaptureState);
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

        public Fixture()
        {
            var tokenHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_token)));
            Session = WhatsAppFlowSession.Create(_contactId, 0, _userId, _actorId, _definitionId, _versionId,
                2, _bindingId, "es", "{\"es\":\"Formulario\"}", "VIS", tokenHash, new string('a', 64), Now.AddMinutes(20));
            Session.SealPayload([1]);
            Mapping =
            [
                Field(0, "shortText", 40),
                Field(1, "longText", 300),
                Field(2, "decimal"),
                Field(3, "date"),
                Field(4, "boolean"),
                Field(5, "choice", choices: [OptionId]),
            ];
            Projection = new(1, "{}", new string('b', 64), "flow-static-7.3-r3", 1, "es", Mapping);
            var replySession = new WhatsAppFlowReplySessionRow(Session.Id, _contactId, _userId, _definitionId,
                _versionId, _bindingId, _operationId, "es", [2], "VIS", tokenHash, [1], Now.AddMinutes(20), true,
                WhatsAppFlowCaptureState.Pending, null);
            Messages = new InMemoryWhatsAppMessageRepository(_locks);
            var sessionReader = new FakeSessionReader(replySession, Messages);
            var sessionRepository = new FakeSessionRepository(Session);
            Sink = new FakeSink();
            var stateService = new WhatsAppFlowReplyStateService(Messages, sessionRepository, new FakeUnitOfWork(), new FakeTimeProvider(Now));
            Service = new WhatsAppFlowReplyService(sessionReader, new FakeReplyProtector(_responses),
                new FakeSessionProtector(_token, _actorId, _definitionId, _bindingId, Projection.Hash),
                new FakeProjectionProtector(Projection), Sink, stateService,
                new FakeTimeProvider(Now), NullLogger<WhatsAppFlowReplyService>.Instance);
        }

        public Guid OptionId { get; } = Guid.NewGuid();
        public IReadOnlyList<WhatsAppFlowProjectionField> Mapping { get; }
        public WhatsAppFlowProjection Projection { get; }
        public WhatsAppFlowSession Session { get; }
        public InMemoryWhatsAppMessageRepository Messages { get; }
        public FakeSink Sink { get; }
        public WhatsAppFlowReplyService Service { get; }

        public WhatsAppMessage AddReply(string waMessageId, IReadOnlyDictionary<string, string> answers)
        {
            var json = JsonSerializer.Serialize(new Dictionary<string, string>(answers, StringComparer.Ordinal)
            {
                ["flow_token"] = _token,
            });
            return AddReplyJson(waMessageId, json);
        }

        public WhatsAppMessage AddReplyJson(string waMessageId, string json)
        {
            _responses.Add(waMessageId, json);
            var message = WhatsAppMessage.Inbound(_contactId, waMessageId, WhatsAppMessageKind.FlowReply,
                null, null, Now, [1]);
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
        InMemoryWhatsAppMessageRepository messages) : IWhatsAppFlowSessionReader
    {
        public Task<IReadOnlyList<WhatsAppPendingFlowReplyRow>> ListPendingFlowRepliesAsync(int limit, CancellationToken cancellationToken) =>
            messages.ListPendingFlowRepliesAsync(limit, cancellationToken);
        public Task<WhatsAppFlowReplySessionRow?> FindReplySessionAsync(Guid contactId, string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult<WhatsAppFlowReplySessionRow?>(contactId == session.ContactId && tokenHash == session.TokenHash ? session : null);
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

    private sealed class FakeSessionProtector(string token, Guid actorId, Guid definitionId,
        Guid bindingId, string expectedHash) : IWhatsAppFlowSessionProtector
    {
        public byte[] Protect(Guid sessionId, Guid contactId, Guid userId, Guid versionId, WhatsAppFlowSessionPayload payload) => throw new NotSupportedException();
        public WhatsAppFlowSessionPayload Read(Guid sessionId, Guid contactId, Guid userId, Guid versionId, byte[] payload) =>
            new(1, contactId, userId, actorId, definitionId, versionId, bindingId, "es", new string('c', 64), 0,
                "1", "flow-name", expectedHash, token, "Abrir", "START", new("waba", "phone", "v25.0", new string('d', 64)));
    }

    private sealed class FakeProjectionProtector(WhatsAppFlowProjection projection) : IWhatsAppFlowProtector
    {
        public byte[] ProtectManifest(Guid operationId, Guid neutralOperationId, Guid versionId, WhatsAppFlowStoredManifest manifest) => throw new NotSupportedException();
        public WhatsAppFlowStoredManifest ReadManifest(Guid operationId, Guid neutralOperationId, Guid versionId, byte[] protectedManifest) => throw new NotSupportedException();
        public byte[] ProtectProjection(Guid operationId, Guid bindingId, Guid versionId, string culture, WhatsAppFlowProjection value) => throw new NotSupportedException();
        public WhatsAppFlowProjection ReadProjection(Guid operationId, Guid bindingId, Guid versionId, string culture, byte[] protectedProjection) => projection;
    }

    private sealed class FakeSink : IStructuredRecordSink
    {
        public StructuredRecordCaptureRequest? Request { get; private set; }
        public int CaptureCount { get; private set; }
        public List<StructuredRecordCaptureResponse> Results { get; } = [];
        public Task<StructuredRecordCaptureResponse> CaptureAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken)
        {
            CaptureCount++;
            Request = request;
            var result = Results.Count == 0 ? new StructuredRecordCaptureResponse("Completed", Guid.NewGuid(), 24, null) : Results[0];
            if (Results.Count != 0) Results.RemoveAt(0);
            return Task.FromResult(result);
        }
    }
}
