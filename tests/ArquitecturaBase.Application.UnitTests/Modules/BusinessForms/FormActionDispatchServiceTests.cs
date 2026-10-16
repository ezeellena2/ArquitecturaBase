using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormActionDispatchServiceTests
{
    [Fact]
    public async Task A_failed_predecessor_blocks_only_its_own_rule()
    {
        var fixture = new FormActionFixture();
        var chained = new FormRule(Guid.NewGuid(), "Received", [], [FormActionFixture.Action(), FormActionFixture.Action()]);
        var independent = new FormRule(Guid.NewGuid(), "Received", [], [FormActionFixture.Action()]);
        var (record, snapshot) = fixture.Capture([chained, independent]);
        await fixture.Recorder.RecordAsync(record, snapshot, "Received", CancellationToken.None);
        var service = Dispatch(fixture);
        var first = fixture.Runs.Runs.Single(run => run.RuleId == chained.Id && run.Order == 0);
        var second = fixture.Runs.Runs.Single(run => run.RuleId == chained.Id && run.Order == 1);
        var other = fixture.Runs.Runs.Single(run => run.RuleId == independent.Id);
        Assert.Null((await service.ReserveAsync(second.Id, CancellationToken.None)).Value);
        var work = (await service.ReserveAsync(first.Id, CancellationToken.None)).Value;
        Assert.NotNull(work);
        Assert.True((await service.CompleteAsync(work, new(FormActionRunState.Failed, FormActionErrors.EffectUnconfirmed.Code, true), CancellationToken.None)).IsSuccess);
        Assert.Null((await service.ReserveAsync(second.Id, CancellationToken.None)).Value);
        Assert.NotNull((await service.ReserveAsync(other.Id, CancellationToken.None)).Value);
        Assert.Equal(FormActionRunState.Waiting, second.State);
        Assert.Equal(0, second.Fence);
    }

    [Fact]
    public async Task Requesting_approval_confirms_task_response_and_run_in_one_transaction()
    {
        var fixture = new FormActionFixture();
        var rule = new FormRule(Guid.NewGuid(), "Received", [], [FormActionFixture.Action("RequestApproval") with { RoleId = fixture.Rules.RoleId }]);
        var (record, snapshot) = fixture.Capture([rule]);
        await fixture.Recorder.RecordAsync(record, snapshot, "Received", CancellationToken.None);
        var run = Assert.Single(fixture.Runs.Runs);
        var boundary = new FakeUnitOfWork { OnCommit = () =>
        {
            var task = Assert.Single(fixture.Runs.Tasks);
            Assert.Equal(record.ActorUserId, task.AuthorUserId);
            Assert.Equal(record.RuleSetId, task.RuleSetId);
            Assert.Equal(fixture.Rules.RoleId, task.RoleId);
            Assert.Equal(FormRecordState.PendingApproval, record.State);
            Assert.Equal(FormActionRunState.Completed, run.State);
        }};
        var service = Dispatch(fixture, boundary);
        Assert.Null((await service.ReserveAsync(run.Id, CancellationToken.None)).Value);
        Assert.Equal(1, boundary.Transactions);
        Assert.Equal(1, boundary.Commits);
        Assert.False(boundary.InTransaction);
        Assert.Equal(0, fixture.Pdf.Prepared);
        Assert.Empty(fixture.Pdf.Executed);
    }

    [Fact]
    public async Task Another_matching_approval_rule_fails_without_opening_a_second_task()
    {
        var fixture = new FormActionFixture();
        var rules = Enumerable.Range(0, 2).Select(_ => new FormRule(Guid.NewGuid(), "Received", [],
            [FormActionFixture.Action("RequestApproval") with { RoleId = fixture.Rules.RoleId }])).ToArray();
        var (record, snapshot) = fixture.Capture(rules);
        await fixture.Recorder.RecordAsync(record, snapshot, "Received", CancellationToken.None);
        var service = Dispatch(fixture);
        foreach (var run in fixture.Runs.Runs) Assert.Null((await service.ReserveAsync(run.Id, CancellationToken.None)).Value);
        Assert.Single(fixture.Runs.Tasks);
        Assert.Equal(FormRecordState.PendingApproval, record.State);
        var failed = Assert.Single(fixture.Runs.Runs, run => run.State == FormActionRunState.Failed);
        Assert.Equal(FormActionErrors.ApprovalAlreadyPending.Code, failed.FailureCode);
        Assert.False(failed.CanRetry);
    }

    [Fact]
    public async Task A_stale_pdf_reservation_cannot_confirm_metadata_or_repeat_a_confirmed_copy()
    {
        var fixture = new FormActionFixture();
        var rule = new FormRule(Guid.NewGuid(), "Approved", [], [FormActionFixture.Action("GeneratePdf")]);
        var (record, snapshot) = fixture.Capture([rule]);
        snapshot = snapshot with { Decision = new(Guid.NewGuid(), Guid.NewGuid(), "Supervisor", fixture.Clock.GetUtcNow().UtcDateTime, "Approved", null) };
        await fixture.Recorder.RecordAsync(record, snapshot, "Approved", CancellationToken.None);
        var documents = new MemoryFormDocumentRepository();
        var files = new MemoryStoredFileRepository();
        var service = Dispatch(fixture, documents: documents, files: files);
        var run = Assert.Single(fixture.Runs.Runs);
        var late = (await service.ReserveAsync(run.Id, CancellationToken.None)).Value!;
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        var current = (await service.ReserveAsync(run.Id, CancellationToken.None)).Value!;
        Assert.True(current.ReconciliationOnly);
        Assert.True(current.Fence > late.Fence);
        var pdf = new FormPdfActionOutcomeResponse(FormPdfActionState.Completed,
            [new("es", "fixed/es", "Visita.pdf", "application/pdf", 2, new string('a', 64)),
             new("en", "fixed/en", "Visit.pdf", "application/pdf", 2, new string('b', 64))]);
        var outcome = new FormActionOutcomeResponse(FormActionRunState.Completed, Pdf: pdf);
        Assert.True((await service.CompleteAsync(late, outcome, CancellationToken.None)).IsFailure);
        Assert.Empty(documents.Documents);
        Assert.Empty(files.Files);
        Assert.True((await service.CompleteAsync(current, outcome, CancellationToken.None)).IsSuccess);
        Assert.True((await service.CompleteAsync(current, outcome, CancellationToken.None)).IsSuccess);
        Assert.Equal(2, documents.Documents.Count);
        Assert.Equal(2, files.Files.Count);
        Assert.Equal(FormActionRunState.Completed, run.State);
    }

    private static FormActionDispatchService Dispatch(FormActionFixture fixture, FakeUnitOfWork? boundary = null,
        MemoryFormDocumentRepository? documents = null, MemoryStoredFileRepository? files = null) =>
        new(fixture.Runs, fixture.Runs, fixture.Rules, boundary ?? fixture.Rules.UnitOfWork,
            new FormPdfDocumentRecorder(documents ?? new(), files ?? new()), fixture.Clock, NullLogger<FormActionDispatchService>.Instance);
}
