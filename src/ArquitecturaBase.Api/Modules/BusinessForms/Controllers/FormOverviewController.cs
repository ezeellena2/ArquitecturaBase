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
[Route("api/forms/overview")]
[Tags("Form overview")]
[HasAnyPermission(BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.ApprovalsDecide)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormOverviewController(IFormOverviewService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FormOverviewResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] FormOverviewQuery query, CancellationToken cancellationToken) =>
        (await service.ListAsync(new() { Page = query.Page, PageSize = query.PageSize, Search = query.Search, Sort = query.Sort,
            State = query.State, PendingOnly = query.PendingOnly }, cancellationToken)).ToActionResult(this);
}