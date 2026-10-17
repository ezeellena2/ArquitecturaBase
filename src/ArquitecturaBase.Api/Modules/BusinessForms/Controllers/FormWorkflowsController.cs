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
[HasPermission(BusinessFormsPermissions.RecordsRead)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class FormWorkflowsController(IFormWorkflowService service):ControllerBase
{
    [HttpGet("workflow")]
    [ProducesResponseType<FormWorkflowResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute]Guid recordId,CancellationToken cancellationToken)=>
        (await service.GetAsync(recordId,cancellationToken)).ToActionResult(this);
    [HttpPost("actions/{runId:guid}/retry")]
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
    [HttpPost("actions/{runId:guid}/review")]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [HasPermission(BusinessFormsPermissions.RulesManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormWorkflowResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Review([FromRoute]Guid recordId,[FromRoute]Guid runId,[FromBody]ReviewFormActionHttpRequest request,CancellationToken cancellationToken)
    {
        var result=await service.ReviewAsync(recordId,runId,request.ExpectedRevision,request.WasSent,cancellationToken);
        if(result.IsSuccess)Response.Headers.Location=$"/api/forms/records/{recordId}/workflow";
        return result.ToAcceptedResult(this);
    }
    [HttpPost("actions/{runId:guid}/consult")]
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
