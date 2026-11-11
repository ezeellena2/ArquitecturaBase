using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormRecordVoidServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 11, 25, 0, TimeSpan.Zero);

    [Fact]
    public async Task Whoever_loaded_a_response_voids_it_before_a_decision_and_its_open_approval_closes()
    {
        var fixture = new Fixture();
        var record = fixture.Record(fixture.Actor, pending: true);
        var task = fixture.OpenTask(record);

        var result = await fixture.Service.VoidAsync(fixture.Repository.DefinitionId, record.Id, new(" Cargada dos veces. "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FormRecordState.Voided, record.State);
        Assert.Equal((fixture.Actor, Now.UtcDateTime, "Cargada dos veces."), (record.VoidedByUserId, record.VoidedAtUtc, record.VoidReason));
        Assert.Equal(FormTaskState.Closed, task.State);
        Assert.Equal(1, fixture.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, fixture.UnitOfWork.LastPolicy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Whoever_loaded_it_does_not_void_it_once_decided(bool approved)
    {
        var fixture = new Fixture();
        var record = fixture.Record(fixture.Actor, pending: true);
        record.CompleteApproval(approved);

        var result = await fixture.Service.VoidAsync(fixture.Repository.DefinitionId, record.Id, new("Motivo"), CancellationToken.None);

        Assert.Equal(FormRecordErrors.AmendUnavailable, result.Error);
        Assert.NotEqual(FormRecordState.Voided, record.State);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task With_the_permission_anyone_voids_any_response_even_approved()
    {
        var fixture = new Fixture([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsAmend]);
        var record = fixture.Record(Guid.NewGuid(), pending: true);
        record.CompleteApproval(true);

        var result = await fixture.Service.VoidAsync(fixture.Repository.DefinitionId, record.Id, new("Lote equivocado"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FormRecordState.Voided, record.State);
    }

    [Fact]
    public async Task Someone_elses_response_does_not_exist_for_whoever_only_loads_and_is_forbidden_for_whoever_reads()
    {
        var loads = new Fixture();
        var foreign = loads.Record(Guid.NewGuid(), pending: false);
        Assert.Equal(FormRecordErrors.NotFound, (await loads.Service.VoidAsync(loads.Repository.DefinitionId, foreign.Id, new("Motivo"), CancellationToken.None)).Error);

        var reads = new Fixture([BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead]);
        var other = reads.Record(Guid.NewGuid(), pending: false);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await reads.Service.VoidAsync(reads.Repository.DefinitionId, other.Id, new("Motivo"), CancellationToken.None)).Error);
        Assert.Equal(FormRecordState.Received, other.State);
    }

    [Fact]
    public async Task Only_viewing_responses_does_not_void_them()
    {
        var fixture = new Fixture([BusinessFormsPermissions.RecordsRead]);
        var record = fixture.Record(fixture.Actor, pending: false);

        Assert.Equal(FormDefinitionErrors.Forbidden,
            (await fixture.Service.VoidAsync(fixture.Repository.DefinitionId, record.Id, new("Motivo"), CancellationToken.None)).Error);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("501")]
    public async Task The_reason_is_checked_before_opening_the_transaction(string? reason)
    {
        var fixture = new Fixture();
        var record = fixture.Record(fixture.Actor, pending: false);

        var result = await fixture.Service.VoidAsync(fixture.Repository.DefinitionId, record.Id,
            new(reason == "501" ? new string('a', 501) : reason), CancellationToken.None);

        Assert.Equal(ArquitecturaBase.Domain.Results.ErrorType.Validation, result.Error.Type);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
        Assert.Equal(FormRecordState.Received, record.State);
    }

    [Fact]
    public async Task A_replaced_or_voided_response_conflicts_and_a_response_of_another_form_is_missing()
    {
        var fixture = new Fixture([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsAmend]);
        var record = fixture.Record(Guid.NewGuid(), pending: false);
        Assert.True(record.Void(Guid.NewGuid(), "Antes", Now.UtcDateTime).IsSuccess);

        Assert.Equal(FormRecordErrors.AmendUnavailable,
            (await fixture.Service.VoidAsync(fixture.Repository.DefinitionId, record.Id, new("Otra vez"), CancellationToken.None)).Error);
        Assert.Equal(FormRecordErrors.NotFound,
            (await fixture.Service.VoidAsync(Guid.NewGuid(), fixture.Record(Guid.NewGuid(), pending: false).Id, new("Motivo"), CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Losing_the_permission_inside_the_transaction_voids_nothing()
    {
        var fixture = new Fixture([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsAmend]);
        var record = fixture.Record(Guid.NewGuid(), pending: false);
        fixture.UnitOfWork.OnStart = () => fixture.Permissions.Permissions[fixture.Actor] = [BusinessFormsPermissions.RecordsRead];

        var result = await fixture.Service.VoidAsync(fixture.Repository.DefinitionId, record.Id, new("Motivo"), CancellationToken.None);

        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(FormRecordState.Received, record.State);
        Assert.Equal(0, fixture.UnitOfWork.Commits);
    }

    [Fact]
    public async Task A_response_with_an_issuance_starts_its_reversal_instead_of_voiding_now()
    {
        var fixture = new Fixture([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsAmend, "test.issue"]);
        var record = fixture.Record(Guid.NewGuid(), pending: false);
        var issued = fixture.Issuance(record);

        var result = await fixture.Service.VoidAsync(fixture.Repository.DefinitionId, record.Id, new("Se cargó mal"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FormRecordState.Received, record.State);
        var reversal = Assert.Single(fixture.Runs.Runs, run => run.Event == "Voided");
        var snapshot = FormDraftPolicy.Read<FormActionSnapshot>(reversal.SnapshotJson);
        Assert.Equal((issued.Id, "Se cargó mal", fixture.Actor, "test.issuer"), (snapshot.ReversesRunId!.Value, snapshot.VoidReason!, snapshot.VoidedByUserId!.Value, snapshot.IssuerId!));
        Assert.Equal(FormRecordErrors.Voiding, (await fixture.Service.VoidAsync(fixture.Repository.DefinitionId, record.Id, new("Otra vez"), CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Reversing_takes_the_permission_of_the_issuer_and_a_failed_issuance_voids_now()
    {
        var withoutIssue = new Fixture([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsAmend]);
        var issuedRecord = withoutIssue.Record(Guid.NewGuid(), pending: false);
        withoutIssue.Issuance(issuedRecord);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await withoutIssue.Service.VoidAsync(withoutIssue.Repository.DefinitionId, issuedRecord.Id, new("Motivo"), CancellationToken.None)).Error);

        var failedRecord = withoutIssue.Record(Guid.NewGuid(), pending: false);
        withoutIssue.Issuance(failedRecord, FormActionRunState.Failed);
        Assert.True((await withoutIssue.Service.VoidAsync(withoutIssue.Repository.DefinitionId, failedRecord.Id, new("Motivo"), CancellationToken.None)).IsSuccess);
        Assert.Equal(FormRecordState.Voided, failedRecord.State);
    }

    private sealed class Fixture
    {
        public FakeRecordRepository Repository { get; } = FakeRecordRepository.Empty();
        public InMemoryUserAccounts Users { get; } = new();
        public FakeCurrentUser Current { get; } = new();
        public FakePermissionService Permissions { get; } = new();
        public StartingUnitOfWork UnitOfWork { get; } = new();
        public Guid Actor { get; }
        public MemoryFormActionRuns Runs { get; } = new();
        public VoidIssuer Issuer { get; } = new();
        public FormRecordVoidService Service { get; }

        public BusinessFormActionRun Issuance(BusinessFormRecord record, FormActionRunState state = FormActionRunState.Completed)
        {
            var run = BusinessFormActionRun.Create(record.DefinitionId, record.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Received", "IssueDocument", 0,
                null, FormDraftPolicy.Serialize(new FormActionSnapshot(null, null, null, [], IssuerId: "test.issuer")), true, Now.UtcDateTime);
            if (state != FormActionRunState.Waiting)
            {
                var lease = run.Reserve(Now.UtcDateTime, TimeSpan.FromMinutes(2)).Value;
                run.RecordOutcome(lease.Fence, Now.UtcDateTime, state, state == FormActionRunState.Failed ? "test.rejected" : null, false);
            }
            Runs.Runs.Add(run);
            return run;
        }

        public Fixture(string[]? permissions = null)
        {
            Actor = Users.AddUser("void@example.invalid").Id;
            Current.UserId = Actor;
            Permissions.Permissions[Actor] = permissions ?? [BusinessFormsPermissions.RecordsCreate];
            Service = new(new(Current, Permissions, Users), Repository, UnitOfWork,
                RequestValidators.For(new VoidFormRecordRequestValidator()), new FakeTimeProvider(Now), NullLogger<FormRecordVoidService>.Instance, Runs, [Issuer]);
        }

        public BusinessFormRecord Record(Guid author, bool pending)
        {
            var record = BusinessFormRecord.Create(Repository.DefinitionId, Guid.NewGuid(), Repository.Records.Count + 1, author, Guid.NewGuid(),
                new DateTime(2026, 11, 5, 10, 42, 0, DateTimeKind.Utc), "[]");
            if (pending) record.MarkPendingApproval();
            Repository.Records.Add(record);
            return record;
        }

        public BusinessFormTask OpenTask(BusinessFormRecord record)
        {
            var task = BusinessFormTask.Open(record.DefinitionId, record.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), record.ActorUserId,
                new DateTime(2026, 11, 5, 10, 43, 0, DateTimeKind.Utc));
            Repository.Tasks.Add(task);
            return task;
        }
    }

    private sealed class VoidIssuer : ArquitecturaBase.Application.Interfaces.Channels.IStructuredRecordIssuer
    {
        public string IssuerId => "test.issuer";
        public IReadOnlyDictionary<string, string> Name { get; } = new Dictionary<string, string> { ["es"] = "Emitir" };
        public string StandardKey => "test.standard";
        public string ReviewPermission => "test.issue";
        public IReadOnlyList<ArquitecturaBase.Application.Models.StructuredData.StructuredRecordResultField> ResultFields => [];
        public Task<ArquitecturaBase.Application.Models.StructuredData.StructuredIssueResponse> EnqueueAsync(
            ArquitecturaBase.Application.Models.StructuredData.StructuredIssueRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ArquitecturaBase.Application.Models.StructuredData.StructuredIssueResponse> GetAsync(Guid runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ArquitecturaBase.Application.Models.StructuredData.StructuredIssueResponse> ReviewAsync(Guid runId,
            ArquitecturaBase.Application.Models.StructuredData.StructuredIssueReview review, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ArquitecturaBase.Application.Models.StructuredData.StructuredIssuePreview> PreviewAsync(
            IReadOnlyList<ArquitecturaBase.Application.Models.StructuredData.StructuredIssueDatum> data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ArquitecturaBase.Application.Models.StructuredData.StructuredIssueResponse> CancelAsync(Guid runId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ArquitecturaBase.Application.Models.StructuredData.StructuredIssueResponse> EnqueueReversalAsync(Guid reversalRunId, Guid issuedRunId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>The fake unit of work, with a hook at the start of the work to change permissions inside the transaction.</summary>
    private sealed class StartingUnitOfWork : IUnitOfWork
    {
        private readonly FakeUnitOfWork _inner = new();
        public Action? OnStart { get; set; }
        public int Commits => _inner.Commits;
        public int Transactions => _inner.Transactions;
        public CommitPolicy? LastPolicy => _inner.LastPolicy;
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CommitPolicy policy,
            CancellationToken cancellationToken) where TResult : ArquitecturaBase.Domain.Results.Result =>
            _inner.ExecuteInTransactionAsync(ct => { OnStart?.Invoke(); return work(ct); }, policy, cancellationToken);
    }
}
