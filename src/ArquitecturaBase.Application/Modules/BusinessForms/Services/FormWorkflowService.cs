using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormWorkflowService(IFormActionRunReader reader,IFormActionRunRepository runs,IFormDraftRepository drafts,
    FormRecordAccessGuard access,IPermissionService permissions,IUnitOfWork unitOfWork,TimeProvider timeProvider,ILogger<FormWorkflowService> logger):IFormWorkflowService
{
    public Task<Result<FormWorkflowResponse>> GetAsync(Guid recordId,CancellationToken cancellationToken)=>OperationLog.RunAsync<Result<FormWorkflowResponse>>(logger,"ReadFormWorkflow",async()=>
    {
        var actor=await access.CheckAsync(cancellationToken);if(actor.IsFailure)return actor.Error;
        var result=await reader.FindWorkflowAsync(recordId,cancellationToken);if(result is null)return FormRecordErrors.NotFound;
        var manage=await permissions.HasPermissionAsync(actor.Value,BusinessFormsPermissions.RulesManage,cancellationToken);
        if(!await access.StillAllowedAsync(actor.Value,cancellationToken))return FormDefinitionErrors.Forbidden;
        return result with{Actions=result.Actions.Select(action=>action with{Reason=action.ReasonCode is {} code?ErrorMessages.Get(code):null,CanRetry=manage&&action.CanRetry,CanConsult=manage&&action.CanConsult}).ToArray()};
    });
    public Task<Result<FormWorkflowResponse>> RetryAsync(Guid recordId,Guid runId,CancellationToken cancellationToken)=>
        OperationLog.RunAsync<Result<FormWorkflowResponse>>(logger,"RetryFormAction",()=>ChangeAsync(recordId,runId,true,cancellationToken));
    public Task<Result<FormWorkflowResponse>> ConsultAsync(Guid recordId,Guid runId,CancellationToken cancellationToken)=>
        OperationLog.RunAsync<Result<FormWorkflowResponse>>(logger,"ConsultFormAction",()=>ChangeAsync(recordId,runId,false,cancellationToken));
    private async Task<Result<FormWorkflowResponse>> ChangeAsync(Guid recordId,Guid runId,bool retry,CancellationToken cancellationToken)
    {
        var actor=await access.CheckAsync(cancellationToken);if(actor.IsFailure)return actor.Error;
        if(!await permissions.HasPermissionAsync(actor.Value,BusinessFormsPermissions.RulesManage,cancellationToken))return FormDefinitionErrors.Forbidden;
        var changed=await unitOfWork.ExecuteInTransactionAsync(async ct=>
        {
            await drafts.LockDefinitionsAsync(ct);
            if(!await access.StillAllowedAsync(actor.Value,ct)||!await permissions.HasPermissionAsync(actor.Value,BusinessFormsPermissions.RulesManage,ct))return Result.Failure(FormDefinitionErrors.Forbidden);
            var run=await runs.GetAsync(runId,ct);if(run is null||run.RecordId!=recordId)return Result.Failure(FormActionErrors.NotFound);
            return retry?run.RequestRetry(timeProvider.GetUtcNow().UtcDateTime):run.RequestConsult(timeProvider.GetUtcNow().UtcDateTime);
        },CommitPolicy.OnSuccess,cancellationToken);
        return changed.IsFailure?changed.Error:await GetAsync(recordId,cancellationToken);
    }
}
