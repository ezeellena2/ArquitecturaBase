using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormActionDispatchService(IFormActionRunRepository runs,IFormActionRunReader reader,IFormDraftRepository drafts,IFormRuleReader ruleReader,
    IUnitOfWork unitOfWork,FormActionOutcomeRecorder outcomes,TimeProvider timeProvider,ILogger<FormActionDispatchService> logger):IFormActionDispatchService
{
    public async Task<IReadOnlyList<Guid>> ListDueIdsAsync(CancellationToken cancellationToken)
    {
        OperationLog.Handling(logger,"ListDueFormActions");var result=await reader.ListDueIdsAsync(timeProvider.GetUtcNow().UtcDateTime,cancellationToken);
        OperationLog.Handled(logger,"ListDueFormActions");return result;
    }
    public Task<Result<FormActionWorkResponse?>> ReserveAsync(Guid runId,CancellationToken cancellationToken)=>
        OperationLog.RunAsync<Result<FormActionWorkResponse?>>(logger,"ReserveFormAction",()=>unitOfWork.ExecuteInTransactionAsync(ct=>ReserveCoreAsync(runId,ct),CommitPolicy.OnSuccess,cancellationToken));
    private async Task<Result<FormActionWorkResponse?>> ReserveCoreAsync(Guid runId,CancellationToken ct)
    {
        await drafts.LockDefinitionsAsync(ct);var run=await runs.GetAsync(runId,ct);if(run is null)return Result.Success<FormActionWorkResponse?>(null);
        if(run.DependsOnRunId is {} dependency)
        {
            var previous=await runs.GetAsync(dependency,ct);
            if(previous?.State is not (FormActionRunState.Completed or FormActionRunState.NotApplicable))return Result.Success<FormActionWorkResponse?>(null);
        }
        var now=timeProvider.GetUtcNow().UtcDateTime;var reserved=run.Reserve(now,TimeSpan.FromMinutes(2));if(reserved.IsFailure)return Result.Success<FormActionWorkResponse?>(null);
        var snapshot=FormDraftPolicy.Read<FormActionSnapshot>(run.SnapshotJson);
        if(snapshot.PreparationFailureCode is {} failure){run.RecordOutcome(reserved.Value.Fence,now,FormActionRunState.Failed,failure,false);return Result.Success<FormActionWorkResponse?>(null);}
        if(run.Kind=="RequestApproval")
        {
            // A response corrected or voided before its approval was asked no longer waits for one: its correction asks again.
            var target=await runs.GetRecordAsync(run.RecordId,ct)??throw new InvalidOperationException("The action response is missing.");
            if(!target.CanBeAmended)
            {
                var skipped=run.RecordOutcome(reserved.Value.Fence,now,FormActionRunState.NotApplicable,null,false);
                return skipped.IsFailure?Result.Failure<FormActionWorkResponse?>(skipped.Error):Result.Success<FormActionWorkResponse?>(null);
            }
            var availableRoles=await ruleReader.ListRolesAsync(ct);
            if(!availableRoles.Any(role=>role.Id==snapshot.ApprovalRoleId))
            {
                run.RecordOutcome(reserved.Value.Fence,now,FormActionRunState.Failed,FormRuleErrors.RoleUnavailable.Code,false);
                return Result.Success<FormActionWorkResponse?>(null);
            }
            var pending=await runs.GetOpenTaskAsync(run.RecordId,ct);
            if(pending is not null){run.RecordOutcome(reserved.Value.Fence,now,FormActionRunState.Failed,FormActionErrors.ApprovalAlreadyPending.Code,false);return Result.Success<FormActionWorkResponse?>(null);}
            var record=await runs.GetRecordAsync(run.RecordId,ct)??throw new InvalidOperationException("The action response is missing.");
            var marked=record.MarkPendingApproval();if(marked.IsFailure){run.RecordOutcome(reserved.Value.Fence,now,FormActionRunState.Failed,marked.Error.Code,false);return Result.Success<FormActionWorkResponse?>(null);}
            runs.AddTask(BusinessFormTask.Open(run.DefinitionId,run.RecordId,run.RuleSetId,run.Id,snapshot.ApprovalRoleId!.Value,record.ActorUserId,now));
            var completed=run.RecordOutcome(reserved.Value.Fence,now,FormActionRunState.Completed,null,false);
            return completed.IsFailure?Result.Failure<FormActionWorkResponse?>(completed.Error):Result.Success<FormActionWorkResponse?>(null);
        }
        // An issuance is queued only while its response stands; once queued, it is followed to its end.
        var issued=run.Kind=="IssueDocument"?await runs.GetRecordAsync(run.RecordId,ct)??throw new InvalidOperationException("The action response is missing."):null;
        var stands=issued?.CanBeAmended??true;
        IReadOnlyList<FormPdfArtifact> artifacts=[];
        if(snapshot.PdfRunId is {} pdfId)
        {
            var pdfRun=await runs.GetAsync(pdfId,ct)??throw new InvalidOperationException("The dependent PDF execution is missing.");
            var pdfOutcome=pdfRun.OutcomeJson is null?null:FormDraftPolicy.Read<FormActionOutcomeResponse>(pdfRun.OutcomeJson);
            artifacts=pdfOutcome?.Pdf?.Artifacts??(pdfOutcome?.Issue?.Document is {} fiscal?FormActionOutcomeRecorder.Artifacts(fiscal):[]);
        }
        return new FormActionWorkResponse(run.Id,run.RecordId,run.Kind,reserved.Value.Fence,reserved.Value.ReconciliationOnly,run.RetryRequested,snapshot,artifacts,
            run.DeliveryReviewRequested?new StructuredRecordActionReview(run.DeliveryReviewExpectedRevision!.Value,run.DeliveryReviewWasSent!.Value,run.DeliveryReviewedByUserId!.Value,run.DeliveryReviewedAtUtc!.Value):null,run.DeliveryReviewRecipientUserId,
            run.OutcomeJson is null?null:FormDraftPolicy.Read<FormActionOutcomeResponse>(run.OutcomeJson).Recipients,run.RetryRecipientUserId)
            { RecordStands=stands,RecordEnteredInApp=issued?.EnteredInApp??true,PriorIssue=run.OutcomeJson is null?null:FormDraftPolicy.Read<FormActionOutcomeResponse>(run.OutcomeJson).Issue };
    }
    public Task<Result> CompleteAsync(FormActionWorkResponse work,FormActionOutcomeResponse outcome,CancellationToken cancellationToken)=>
        OperationLog.RunAsync(logger,"CompleteFormAction",()=>unitOfWork.ExecuteInTransactionAsync(async ct=>
        {
            await drafts.LockDefinitionsAsync(ct);var run=await runs.GetAsync(work.RunId,ct);var now=timeProvider.GetUtcNow().UtcDateTime;
            if(run?.State==FormActionRunState.Completed&&run.Fence==work.Fence)return Result.Success();
            if(run is null||run.RecordId!=work.RecordId||!run.OwnsLease(work.Fence,now))return FormActionErrors.StateConflict;
            if(outcome.State==FormActionRunState.Completed||outcome.State==FormActionRunState.NotApplicable&&work.Snapshot.ReversesRunId is not null)
                await outcomes.AddAsync(run,work.Snapshot,outcome,now,ct);
            return run.RecordOutcome(work.Fence,now,outcome.State,outcome.FailureCode,outcome.CanRetry,FormDraftPolicy.Serialize(outcome),outcome.Issue?.NextCheckAtUtc);
        },CommitPolicy.OnSuccess,cancellationToken));
}
