using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
[ApiController]
[Route("api/forms/records/{recordId:guid}")]
[Tags("Form workflow")]
// «Completar formularios» alone reads where the person's own response stands (CW6-C) and confirms the issuance of a response the
// person loaded («¿La emito?»); retrying, reviewing and consulting an action also need record read and rule management.
[HasAnyPermission(BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsCreate)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class FormWorkflowsController(IFormWorkflowService service,IFormIssueConfirmationService confirmations):ControllerBase
{
    [HttpGet("workflow")]
    [ProducesResponseType<FormWorkflowResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute]Guid recordId,CancellationToken cancellationToken)=>
        (await service.GetAsync(recordId,cancellationToken)).ToActionResult(this);
    [HttpPost("actions/{runId:guid}/retry")]
    [HasPermission(BusinessFormsPermissions.RecordsRead)]
    [HasPermission(BusinessFormsPermissions.RulesManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormWorkflowResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry([FromRoute]Guid recordId,[FromRoute]Guid runId,CancellationToken cancellationToken)
    {
        var result=await service.RetryAsync(recordId,runId,cancellationToken);
        if(result.IsSuccess)Response.Headers.Location=$"/api/forms/records/{recordId}/workflow";
        return result.ToAcceptedResult(this);
    }
    [HttpPost("actions/{runId:guid}/recipients/{recipientUserId:guid}/retry")]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [HasPermission(BusinessFormsPermissions.RecordsRead)]
    [HasPermission(BusinessFormsPermissions.RulesManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormWorkflowResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetryRecipient([FromRoute]Guid recordId,[FromRoute]Guid runId,[FromRoute]Guid recipientUserId,[FromBody]RetryFormActionRecipientHttpRequest request,CancellationToken cancellationToken)
    {
        var result=await service.RetryRecipientAsync(recordId,runId,recipientUserId,request.ExpectedRevision,cancellationToken);
        if(result.IsSuccess)Response.Headers.Location=$"/api/forms/records/{recordId}/workflow";
        return result.ToAcceptedResult(this);
    }
    [HttpPost("actions/{runId:guid}/review")]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [HasPermission(BusinessFormsPermissions.RecordsRead)]
    [HasPermission(BusinessFormsPermissions.RulesManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormWorkflowResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Review([FromRoute]Guid recordId,[FromRoute]Guid runId,[FromBody]ReviewFormActionHttpRequest request,CancellationToken cancellationToken)
    {
        var result=await service.ReviewAsync(recordId,runId,request.RecipientUserId,request.ExpectedRevision,request.WasSent,cancellationToken);
        if(result.IsSuccess)Response.Headers.Location=$"/api/forms/records/{recordId}/workflow";
        return result.ToAcceptedResult(this);
    }
    // «¿La emito?» (EM2-A): whoever loaded the response from a channel confirms the issuance, tries a summary that failed again, or voids it without issuing.
    [HttpPost("actions/{runId:guid}/confirmation")]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [RequestSizeLimit(8192)]
    [ProducesResponseType<FormWorkflowResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status403Forbidden)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm([FromRoute]Guid recordId,[FromRoute]Guid runId,[FromBody]ConfirmFormIssueHttpRequest request,CancellationToken cancellationToken)
    {
        var result=await confirmations.ConfirmAsync(recordId,runId,request.Issue,request.Retry,cancellationToken);
        if(result.IsSuccess)Response.Headers.Location=$"/api/forms/records/{recordId}/workflow";
        return result.ToAcceptedResult(this);
    }
    [HttpPost("actions/{runId:guid}/consult")]
    [HasPermission(BusinessFormsPermissions.RecordsRead)]
    [HasPermission(BusinessFormsPermissions.RulesManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormWorkflowResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Consult([FromRoute]Guid recordId,[FromRoute]Guid runId,CancellationToken cancellationToken)
    {
        var result=await service.ConsultAsync(recordId,runId,cancellationToken);
        if(result.IsSuccess)Response.Headers.Location=$"/api/forms/records/{recordId}/workflow";
        return result.ToAcceptedResult(this);
    }
}
