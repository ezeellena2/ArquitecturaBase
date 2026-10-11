using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormRecordSinkTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task A_replayed_session_returns_the_same_record_and_other_answers_do_not_consume_another_number()
    {
        var fixture = new Fixture("shortText", required: false);
        var answers = new Dictionary<string, string> { [fixture.Key] = "answer" };
        var request = fixture.Request(answers);

        var first = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        var replay = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        var different = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string> { [fixture.Key] = "other" }), CancellationToken.None);

        Assert.Equal("Completed", first.State);
        Assert.Equal(first.RecordId, replay.RecordId);
        Assert.Equal(first.Number, replay.Number);
        Assert.Equal("Rejected", different.State);
        Assert.Equal(1, fixture.Definition.LastRecordNumber);
        Assert.Single(fixture.Repository.Records);
    }

    [Fact]
    public async Task A_prior_receipt_can_be_recovered_without_permission_or_answer_values()
    {
        var fixture = new Fixture("shortText", required: false);
        var request = fixture.Request(new Dictionary<string, string> { [fixture.Key] = "private answer" });
        var captured = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        fixture.Permission.Allowed = false;

        var receipt = await fixture.Sink.FindExistingReceiptAsync(request, CancellationToken.None);
        var replay = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        var changedAnswers = await fixture.Sink.FindExistingReceiptAsync(request with
        {
            Answers = new Dictionary<string, string> { [fixture.Key] = "different answer" },
        }, CancellationToken.None);

        Assert.Equal("Completed", receipt!.State);
        Assert.Equal(captured.RecordId, receipt.RecordId);
        Assert.Equal(captured.Number, receipt.Number);
        Assert.Equal(captured, replay);
        Assert.Null(changedAnswers);
        Assert.DoesNotContain("private answer", receipt.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, fixture.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task A_required_text_answer_cannot_be_empty_and_does_not_allocate_a_number()
    {
        var fixture = new Fixture("shortText", required: true);
        var result = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string> { [fixture.Key] = "" }), CancellationToken.None);

        Assert.Equal("Rejected", result.State);
        Assert.Null(result.Number);
        Assert.Equal(0, fixture.Definition.LastRecordNumber);
        Assert.Empty(fixture.Repository.Records);
    }

    [Fact]
    public async Task A_decimal_with_more_precision_than_decimal_can_represent_is_rejected_without_rounding_or_numbering()
    {
        var fixture = new Fixture("decimal", required: true);
        var result = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string>
        {
            [fixture.Key] = "0.123456789012345678901234567890123456789",
        }), CancellationToken.None);

        Assert.Equal("Rejected", result.State);
        Assert.Equal(0, fixture.Definition.LastRecordNumber);
        Assert.Empty(fixture.Repository.Records);
    }

    [Fact]
    public async Task An_exact_decimal_at_scale_28_is_preserved()
    {
        var fixture = new Fixture("decimal", required: true);
        var result = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string>
        {
            [fixture.Key] = "0.0000000000000000000000000001",
        }), CancellationToken.None);

        Assert.Equal("Completed", result.State);
        Assert.Equal(0.0000000000000000000000000001m, Assert.Single(fixture.Repository.Values).DecimalValue);
    }

    [Fact]
    public async Task Boolean_false_is_saved_as_a_value()
    {
        var fixture = new Fixture("boolean", required: true);
        var result = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string> { [fixture.Key] = "false" }), CancellationToken.None);

        Assert.Equal("Completed", result.State);
        Assert.False(Assert.Single(fixture.Repository.Values).BooleanValue);
    }

    [Fact]
    public async Task An_optional_missing_answer_is_absent_from_the_typed_projection()
    {
        var fixture = new Fixture("shortText", required: false);
        var result = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string>()), CancellationToken.None);

        Assert.Equal("Completed", result.State);
        Assert.Empty(fixture.Repository.Values);
        Assert.Equal("[]", Assert.Single(fixture.Repository.Records).ValuesJson);
    }

    [Fact]
    public async Task A_permission_revoked_after_the_request_check_is_rejected_inside_the_capture_transaction()
    {
        var fixture = new Fixture("shortText", required: false);
        fixture.Permission.RevokeOnSecondCheck = true;
        var result = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string> { [fixture.Key] = "answer" }), CancellationToken.None);

        Assert.Equal("Rejected", result.State);
        Assert.Equal(FormRecordErrors.AuthorizationChanged.Code, result.ErrorCode);
        Assert.Equal(0, fixture.Definition.LastRecordNumber);
        Assert.Empty(fixture.Repository.Records);
        Assert.Single(fixture.Repository.Receipts);
    }

    private sealed class Fixture
    {
        public Fixture(string type, bool required)
        {
            var fieldId = Guid.NewGuid();
            Key = $"field_{fieldId:N}";
            Definition = BusinessFormDefinition.Create();
            var definition = new Dictionary<string, string> { ["es"] = "Formulario" };
            var schemaDraft = "{}";
            Assert.True(Definition.SetDraft(1, "Formulario", JsonSerializer.Serialize(definition), "FORM", "es",
                "{\"es\":\"Enviar\"}", schemaDraft, 1, creating: true).IsSuccess);
            var version = BusinessFormVersion.Create(Definition.Id, 1, new string('a', 64));
            version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1,
                [new CanonicalFormField(fieldId, Key, type, definition, null, required, 100, null)]), SerializerOptions));
            version.SealPresentation("{\"es\":\"Formulario\"}", "{\"es\":\"Enviar\"}", "es", 1);
            Assert.True(Definition.ActivateVersion(null, version.Id).IsSuccess);
            Repository = new FakeRecordRepository(Definition, version);
            Permission = new MutablePermissionService();
            Permission.Allowed = true;
            ReceiptReader = new FakeCaptureReceiptReader(Repository);
            Sink = new BusinessFormRecordRecorder(Repository, ReceiptReader, Permission, new FakeUnitOfWork(), TimeProvider.System,
                NullLogger<BusinessFormRecordRecorder>.Instance);
            VersionId = version.Id;
        }

        public const string SourcePrefix = "unused";
        public Guid Actor { get; } = Guid.NewGuid();
        public string Key { get; }
        public Guid VersionId { get; }
        public BusinessFormDefinition Definition { get; }
        public FakeRecordRepository Repository { get; }
        public FakeCaptureReceiptReader ReceiptReader { get; }
        public MutablePermissionService Permission { get; }
        public BusinessFormRecordRecorder Sink { get; }
        public StructuredRecordCaptureRequest Request(IReadOnlyDictionary<string, string> answers) =>
            new(Guid.Parse("00000000-0000-7000-8000-000000000123"), Definition.Id, VersionId, Actor,
                DateTime.SpecifyKind(DateTime.Parse("2026-10-11T12:00:00", CultureInfo.InvariantCulture), DateTimeKind.Utc), answers);
    }

    private sealed class FakeCaptureReceiptReader(FakeRecordRepository repository) : IFormCaptureReceiptReader
    {
        public Task<FormCaptureReceiptRow?> FindAsync(Guid sourceReceiptId, CancellationToken cancellationToken)
        {
            var receipt = repository.Receipts.SingleOrDefault(value => value.SourceReceiptId == sourceReceiptId);
            return Task.FromResult(receipt is null ? null : new FormCaptureReceiptRow(receipt.SourceReceiptId,
                receipt.DefinitionId, receipt.VersionId, receipt.Fingerprint, receipt.RecordId, receipt.Number,
                receipt.State, receipt.ErrorCode));
        }
    }

    private sealed class FakeRecordRepository(BusinessFormDefinition definition, BusinessFormVersion version) : IFormRecordRepository
    {
        public List<BusinessFormRecord> Records { get; } = [];
        public List<BusinessFormRecordValue> Values { get; } = [];
        private readonly List<BusinessFormCaptureReceipt> _receipts = [];
        public IReadOnlyList<BusinessFormCaptureReceipt> Receipts => _receipts;
        public Task LockSourceReceiptAsync(Guid sourceReceiptId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LockDefinitionsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<BusinessFormCaptureReceipt?> GetReceiptAsync(Guid sourceReceiptId, CancellationToken cancellationToken) =>
            Task.FromResult(_receipts.SingleOrDefault(value => value.SourceReceiptId == sourceReceiptId));
        public Task<BusinessFormDefinition?> GetDefinitionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<BusinessFormDefinition?>(id == definition.Id ? definition : null);
        public Task<BusinessFormVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<BusinessFormVersion?>(id == version.Id ? version : null);
        public Task<bool> ExistsPublishedAsync(Guid versionId, CancellationToken cancellationToken) => Task.FromResult(versionId == version.Id);
        public void Add(BusinessFormRecord record) => Records.Add(record);
        public void Add(BusinessFormRecordValue value) => Values.Add(value);
        public void Add(BusinessFormCaptureReceipt receipt) => _receipts.Add(receipt);
    }

    private sealed class MutablePermissionService : IPermissionService
    {
        private int _checks;
        public bool Allowed { get; set; }
        public bool RevokeOnSecondCheck { get; set; }
        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>(Allowed ? [BusinessFormsPermissions.RecordsCreate] : []);
        public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _checks);
            return Task.FromResult(Allowed && !(RevokeOnSecondCheck && count >= 2));
        }
        public Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
