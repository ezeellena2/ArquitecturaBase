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

    [Fact]
    public async Task Two_role_recipients_reviewed_differently_finish_without_partial_retry_and_release_the_next_approval()
    {
        var fixture=new FormActionFixture();var notify=FormActionFixture.Action("NotifyRole") with { RoleId=fixture.Rules.RoleId };
        var approval=FormActionFixture.Action("RequestApproval") with { RoleId=fixture.Rules.RoleId };
        var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[notify,approval])]);var first=captured.Record.ActorUserId;var second=Guid.NewGuid();
        fixture.Recipients.People[second]=new(second,"en","Second employee");
        fixture.Rules.Permissions.Permissions[fixture.Rules.User.UserId!.Value]=[BusinessFormsPermissions.RecordsRead,BusinessFormsPermissions.RulesManage];
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        var dispatch=Dispatch(fixture);var roleRun=fixture.Runs.Runs[0];var successor=fixture.Runs.Runs[1];
        var channel=new FakeFormActionChannel();channel.Statuses[first]=new(ArquitecturaBase.Application.Models.StructuredData.StructuredRecordActionState.Uncertain);channel.Statuses[second]=new(ArquitecturaBase.Application.Models.StructuredData.StructuredRecordActionState.Uncertain);
        var effect=new FormActionEffectService(fixture.Pdf,[channel],NullLogger<FormActionEffectService>.Instance);
        var workflow=new FormWorkflowService(fixture.Runs,fixture.Runs,fixture.Rules,new(fixture.Rules.User,fixture.Rules.Permissions),fixture.Rules.Permissions,fixture.Rules.UnitOfWork,fixture.Clock,NullLogger<FormWorkflowService>.Instance);
        async Task FinishRoleAsync()
        {
            var work=(await dispatch.ReserveAsync(roleRun.Id,CancellationToken.None)).Value;Assert.NotNull(work);
            var outcome=await effect.ExecuteAsync(work,CancellationToken.None);Assert.True((await dispatch.CompleteAsync(work,outcome,CancellationToken.None)).IsSuccess);
        }
        await FinishRoleAsync();Assert.Equal(FormActionRunState.Uncertain,roleRun.State);
        Assert.Null((await dispatch.ReserveAsync(successor.Id,CancellationToken.None)).Value);Assert.Empty(fixture.Runs.Tasks);
        var firstRevision=roleRun.Fence;var firstReviewer=fixture.Rules.User.UserId!.Value;var firstReviewedAt=fixture.Clock.GetUtcNow().UtcDateTime;
        Assert.True((await workflow.ReviewAsync(captured.Record.Id,roleRun.Id,first,firstRevision,true,CancellationToken.None)).IsSuccess);
        await FinishRoleAsync();Assert.Equal(FormActionRunState.Uncertain,roleRun.State);
        Assert.Null((await dispatch.ReserveAsync(successor.Id,CancellationToken.None)).Value);Assert.Empty(fixture.Runs.Tasks);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));fixture.Rules.User.UserId=second;
        fixture.Rules.Permissions.Permissions[second]=[BusinessFormsPermissions.RecordsRead,BusinessFormsPermissions.RulesManage];
        var secondReviewedAt=fixture.Clock.GetUtcNow().UtcDateTime;
        Assert.True((await workflow.ReviewAsync(captured.Record.Id,roleRun.Id,second,roleRun.Fence,false,CancellationToken.None)).IsSuccess);
        await FinishRoleAsync();Assert.Equal(FormActionRunState.Completed,roleRun.State);Assert.False(roleRun.CanRetry);
        Assert.True(roleRun.RequestRetry(fixture.Clock.GetUtcNow().UtcDateTime).IsSuccess);Assert.Equal(FormActionRunState.Completed,roleRun.State);
        Assert.Equal(new[]{first,second},channel.Reviews.Select(review=>review.RecipientUserId));
        Assert.Equal(2,channel.Reads.Count(user=>user==first));Assert.Empty(channel.Messages);Assert.Empty(channel.Retries);
        var result=FormDraftPolicy.Read<FormActionOutcomeResponse>(roleRun.OutcomeJson!);
        Assert.True(Assert.Single(result.Recipients!,recipient=>recipient.UserId==first).Status.Review!.WasSent);
        Assert.False(Assert.Single(result.Recipients!,recipient=>recipient.UserId==second).Status.Review!.WasSent);
        Assert.All(result.Recipients!,recipient=>Assert.Null(recipient.Status.Receipt));
        Assert.Equal(firstReviewer,result.Recipients!.Single(recipient=>recipient.UserId==first).Status.Review!.ReviewedByUserId);
        Assert.Equal(firstReviewedAt,result.Recipients!.Single(recipient=>recipient.UserId==first).Status.Review!.ReviewedAtUtc);
        Assert.Equal(second,result.Recipients!.Single(recipient=>recipient.UserId==second).Status.Review!.ReviewedByUserId);
        Assert.Equal(secondReviewedAt,result.Recipients!.Single(recipient=>recipient.UserId==second).Status.Review!.ReviewedAtUtc);
        var fence=roleRun.Fence;var updated=roleRun.UpdatedAtUtc;var storedOutcome=roleRun.OutcomeJson;fixture.Rules.User.UserId=firstReviewer;
        Assert.True((await workflow.ReviewAsync(captured.Record.Id,roleRun.Id,first,firstRevision,true,CancellationToken.None)).IsSuccess);
        Assert.Equal(FormActionRunState.Completed,roleRun.State);Assert.Equal(fence,roleRun.Fence);Assert.Equal(updated,roleRun.UpdatedAtUtc);Assert.Equal(storedOutcome,roleRun.OutcomeJson);
        Assert.Equal(2,channel.Reviews.Count);
        Assert.Equal(FormActionErrors.StateConflict,(await workflow.ReviewAsync(captured.Record.Id,roleRun.Id,first,firstRevision,false,CancellationToken.None)).Error);
        Assert.Equal(FormActionErrors.StateConflict,(await workflow.ReviewAsync(captured.Record.Id,roleRun.Id,first,firstRevision+1,true,CancellationToken.None)).Error);
        Assert.Null((await dispatch.ReserveAsync(successor.Id,CancellationToken.None)).Value);
        Assert.Equal(FormActionRunState.Completed,successor.State);Assert.Single(fixture.Runs.Tasks);Assert.Equal(FormRecordState.PendingApproval,captured.Record.State);
    }

    [Fact]
    public async Task An_oversized_role_message_does_not_block_other_people_or_the_next_approval()
    {
        var fixture=new FormActionFixture();var role=FormActionFixture.Action("NotifyRole") with{RoleId=fixture.Rules.RoleId,Text=new Dictionary<string,string>{{"es","{Visita}"},{"en","Short message"}}};
        var approval=FormActionFixture.Action("RequestApproval") with{RoleId=fixture.Rules.RoleId};
        var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[role,approval])],new string('x',4097));
        var oversized=Guid.NewGuid();fixture.Recipients.People[oversized]=new(oversized,"es","Oversized recipient");
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        var dispatch=Dispatch(fixture);var work=(await dispatch.ReserveAsync(fixture.Runs.Runs[0].Id,CancellationToken.None)).Value;Assert.NotNull(work);
        var channel=new FakeFormActionChannel();var effect=new FormActionEffectService(fixture.Pdf,[channel],NullLogger<FormActionEffectService>.Instance);
        var result=await effect.ExecuteAsync(work,CancellationToken.None);
        Assert.Equal(FormActionRunState.Completed,result.State);Assert.Equal(captured.Record.ActorUserId,Assert.Single(channel.Messages).RecipientUserId);
        var failed=Assert.Single(result.Recipients!,recipient=>recipient.UserId==oversized);Assert.Equal(FormActionErrors.MessageTooLong.Code,failed.Status.FailureCode);
        Assert.Equal(ArquitecturaBase.Application.Models.StructuredData.StructuredRecordActionState.Failed,failed.Status.State);
        Assert.Empty(Assert.Single(work.Snapshot.Recipients,recipient=>recipient.UserId==oversized).Body);
        Assert.True((await dispatch.CompleteAsync(work,result,CancellationToken.None)).IsSuccess);
        Assert.Null((await dispatch.ReserveAsync(fixture.Runs.Runs[1].Id,CancellationToken.None)).Value);Assert.Single(fixture.Runs.Tasks);
    }
    [Fact]
    public async Task A_role_without_installed_channels_records_each_failure_and_releases_the_next_approval()
    {
        var fixture=new FormActionFixture();var role=FormActionFixture.Action("NotifyRole") with{RoleId=fixture.Rules.RoleId};
        var approval=FormActionFixture.Action("RequestApproval") with{RoleId=fixture.Rules.RoleId};
        var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[role,approval])]);var other=Guid.NewGuid();fixture.Recipients.People[other]=new(other,"es","Other employee");
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        var dispatch=Dispatch(fixture);var work=(await dispatch.ReserveAsync(fixture.Runs.Runs[0].Id,CancellationToken.None)).Value;Assert.NotNull(work);
        var effect=new FormActionEffectService(fixture.Pdf,[],NullLogger<FormActionEffectService>.Instance);var result=await effect.ExecuteAsync(work,CancellationToken.None);
        Assert.Equal(FormActionRunState.Completed,result.State);Assert.False(result.CanRetry);
        Assert.Equal(2,result.Recipients!.Count);Assert.All(result.Recipients!,recipient=>
        {Assert.Null(recipient.ChannelId);Assert.Equal(FormActionErrors.ChannelUnavailable.Code,recipient.Status.FailureCode);Assert.Equal(ArquitecturaBase.Application.Models.StructuredData.StructuredRecordActionState.Failed,recipient.Status.State);});
        Assert.True((await dispatch.CompleteAsync(work,result,CancellationToken.None)).IsSuccess);
        Assert.Null((await dispatch.ReserveAsync(fixture.Runs.Runs[1].Id,CancellationToken.None)).Value);Assert.Single(fixture.Runs.Tasks);
    }
    private static FormActionDispatchService Dispatch(FormActionFixture fixture, FakeUnitOfWork? boundary = null,
        MemoryFormDocumentRepository? documents = null, MemoryStoredFileRepository? files = null) =>
        new(fixture.Runs, fixture.Runs, fixture.Rules, boundary ?? fixture.Rules.UnitOfWork,
            new FormPdfDocumentRecorder(documents ?? new(), files ?? new()), fixture.Clock, NullLogger<FormActionDispatchService>.Instance);
}
