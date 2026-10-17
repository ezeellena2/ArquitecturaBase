using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;

[ApiController]
[Route("api/forms/missing-requests")]
[Tags("Missing form requests")]
[HasPermission(BusinessFormsPermissions.Manage)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
public sealed class FormMissingRequestsController(IFormMissingRequestService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MissingFormTopicsPageResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] MissingFormTopicsQuery query,CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.ListAsync(new() { Page = query.Page,PageSize = query.PageSize,Sort = query.Sort,Search = query.Search,State = query.State },cancellationToken)).ToActionResult(this);
    }
    [HttpPost("{id:guid}/discard")]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<MissingFormTopicResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Discard([FromRoute] Guid id,[FromBody] DiscardMissingFormTopicHttpRequest request,CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await service.DiscardAsync(id,new(request.ExpectedRevision),cancellationToken)).ToActionResult(this);
    }
}
