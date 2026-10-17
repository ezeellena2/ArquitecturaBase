using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormWorkflowService(FormWorkflowReadPolicy readPolicy,IFormActionRunRepository runs,IFormDraftRepository drafts,
    FormRecordAccessGuard access,IPermissionService permissions,IUnitOfWork unitOfWork,TimeProvider timeProvider,ILogger<FormWorkflowService> logger):IFormWorkflowService
{
    public Task<Result<FormWorkflowResponse>> GetAsync(Guid recordId,CancellationToken cancellationToken)=>
        OperationLog.RunAsync<Result<FormWorkflowResponse>>(logger,"ReadFormWorkflow",()=>readPolicy.ReadAsync(recordId,cancellationToken));
    public Task<Result<FormWorkflowResponse>> RetryAsync(Guid recordId,Guid runId,CancellationToken cancellationToken)=>
        OperationLog.RunAsync<Result<FormWorkflowResponse>>(logger,"RetryFormAction",()=>ChangeAsync(recordId,runId,true,cancellationToken));
    public Task<Result<FormWorkflowResponse>> ConsultAsync(Guid recordId,Guid runId,CancellationToken cancellationToken)=>
        OperationLog.RunAsync<Result<FormWorkflowResponse>>(logger,"ConsultFormAction",()=>ChangeAsync(recordId,runId,false,cancellationToken));
    public Task<Result<FormWorkflowResponse>> ReviewAsync(Guid recordId,Guid runId,Guid recipientUserId,long expectedRevision,bool wasSent,CancellationToken cancellationToken)=>
        OperationLog.RunAsync<Result<FormWorkflowResponse>>(logger,"ReviewFormAction",()=>ChangeAsync(recordId,runId,false,cancellationToken,wasSent,expectedRevision,recipientUserId));
    private async Task<Result<FormWorkflowResponse>> ChangeAsync(Guid recordId,Guid runId,bool retry,CancellationToken cancellationToken,bool? wasSent=null,long expectedRevision=0,Guid recipientUserId=default)
    {
        var actor=await access.CheckAsync(cancellationToken);if(actor.IsFailure)return actor.Error;
        if(!await permissions.HasPermissionAsync(actor.Value,BusinessFormsPermissions.RulesManage,cancellationToken))return FormDefinitionErrors.Forbidden;
        var changed=await unitOfWork.ExecuteInTransactionAsync(async ct=>
        {
            await drafts.LockDefinitionsAsync(ct);
            if(!await access.StillAllowedAsync(actor.Value,ct)||!await permissions.HasPermissionAsync(actor.Value,BusinessFormsPermissions.RulesManage,ct))return Result.Failure(FormDefinitionErrors.Forbidden);
            var run=await runs.GetAsync(runId,ct);if(run is null||run.RecordId!=recordId)return Result.Failure(FormActionErrors.NotFound);
            var now=timeProvider.GetUtcNow().UtcDateTime;
            if(wasSent is {} decision)
            {
                var snapshot=FormDraftPolicy.Read<FormActionSnapshot>(run.SnapshotJson);
                if(!snapshot.Recipients.Any(recipient=>recipient.UserId==recipientUserId))return Result.Failure(FormActionErrors.StateConflict);
                var replay=run.DeliveryReviewRecipientUserId==recipientUserId&&run.DeliveryReviewExpectedRevision==expectedRevision
                    &&run.DeliveryReviewWasSent==decision&&run.DeliveryReviewedByUserId==actor.Value;
                var recipients=run.OutcomeJson is null?null:FormDraftPolicy.Read<FormActionOutcomeResponse>(run.OutcomeJson).Recipients;
                if(recipients?.Any(recipient=>recipient.UserId==recipientUserId&&recipient.Status.Review is {} review
                    &&review.ExpectedRevision==expectedRevision&&review.WasSent==decision&&review.ReviewedByUserId==actor.Value)==true)
                    return Result.Success();
                if(!replay&&recipients?.Any(recipient=>recipient.UserId==recipientUserId&&recipient.Status.State==ArquitecturaBase.Application.Models.StructuredData.StructuredRecordActionState.Uncertain)!=true)
                    return Result.Failure(FormActionErrors.StateConflict);
                return run.RequestDeliveryReview(recipientUserId,expectedRevision,decision,actor.Value,now);
            }
            return retry?run.RequestRetry(now):run.RequestConsult(now);
        },CommitPolicy.OnSuccess,cancellationToken);
        return changed.IsFailure?changed.Error:await GetAsync(recordId,cancellationToken);
    }
}
