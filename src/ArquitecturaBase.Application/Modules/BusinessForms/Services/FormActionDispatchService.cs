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
internal sealed class FormActionDispatchService(IFormActionRunRepository runs,IFormActionRunReader reader,IFormDraftRepository drafts,
    IUnitOfWork unitOfWork,FormPdfDocumentRecorder documents,TimeProvider timeProvider,ILogger<FormActionDispatchService> logger):IFormActionDispatchService
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
            var pending=await runs.GetOpenTaskAsync(run.RecordId,ct);
            if(pending is not null){run.RecordOutcome(reserved.Value.Fence,now,FormActionRunState.Failed,FormActionErrors.ApprovalAlreadyPending.Code,false);return Result.Success<FormActionWorkResponse?>(null);}
            var record=await runs.GetRecordAsync(run.RecordId,ct)??throw new InvalidOperationException("The action response is missing.");
            var marked=record.MarkPendingApproval();if(marked.IsFailure){run.RecordOutcome(reserved.Value.Fence,now,FormActionRunState.Failed,marked.Error.Code,false);return Result.Success<FormActionWorkResponse?>(null);}
            runs.AddTask(BusinessFormTask.Open(run.DefinitionId,run.RecordId,run.RuleSetId,run.Id,snapshot.ApprovalRoleId!.Value,record.ActorUserId,now));
            var completed=run.RecordOutcome(reserved.Value.Fence,now,FormActionRunState.Completed,null,false);
            return completed.IsFailure?Result.Failure<FormActionWorkResponse?>(completed.Error):Result.Success<FormActionWorkResponse?>(null);
        }
        IReadOnlyList<FormPdfArtifact> artifacts=[];
        if(snapshot.PdfRunId is {} pdfId)
        {
            var pdfRun=await runs.GetAsync(pdfId,ct)??throw new InvalidOperationException("The dependent PDF execution is missing.");
            artifacts=pdfRun.OutcomeJson is null?[]:FormDraftPolicy.Read<FormActionOutcomeResponse>(pdfRun.OutcomeJson).Pdf?.Artifacts??[];
        }
        return new FormActionWorkResponse(run.Id,run.RecordId,run.Kind,reserved.Value.Fence,reserved.Value.ReconciliationOnly,run.RetryRequested,snapshot,artifacts,
            run.DeliveryReviewRequested?new StructuredRecordActionReview(run.DeliveryReviewExpectedRevision!.Value,run.DeliveryReviewWasSent!.Value,run.DeliveryReviewedByUserId!.Value,run.DeliveryReviewedAtUtc!.Value):null,run.DeliveryReviewRecipientUserId,
            run.OutcomeJson is null?null:FormDraftPolicy.Read<FormActionOutcomeResponse>(run.OutcomeJson).Recipients);
    }
    public Task<Result> CompleteAsync(FormActionWorkResponse work,FormActionOutcomeResponse outcome,CancellationToken cancellationToken)=>
        OperationLog.RunAsync(logger,"CompleteFormAction",()=>unitOfWork.ExecuteInTransactionAsync(async ct=>
        {
            await drafts.LockDefinitionsAsync(ct);var run=await runs.GetAsync(work.RunId,ct);var now=timeProvider.GetUtcNow().UtcDateTime;
            if(run?.State==FormActionRunState.Completed&&run.Fence==work.Fence)return Result.Success();
            if(run is null||run.RecordId!=work.RecordId||!run.OwnsLease(work.Fence,now))return FormActionErrors.StateConflict;
            if(outcome.State==FormActionRunState.Completed&&outcome.Pdf is {} pdf)
                await documents.AddAsync(run.DefinitionId,run.RecordId,work.Snapshot.Pdf??throw new InvalidOperationException("The PDF snapshot is missing."),pdf,ct);
            return run.RecordOutcome(work.Fence,now,outcome.State,outcome.FailureCode,outcome.CanRetry,FormDraftPolicy.Serialize(outcome));
        },CommitPolicy.OnSuccess,cancellationToken));
}
