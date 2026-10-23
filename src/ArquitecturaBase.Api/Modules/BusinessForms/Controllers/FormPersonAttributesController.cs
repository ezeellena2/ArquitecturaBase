using System.Globalization;
using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;

[ApiController]
[Route("api/forms/person-attributes")]
[Tags("Form person data")]
[HasAnyPermission(BusinessFormsPermissions.PersonDataManage, Permissions.Users.Manage, BusinessFormsPermissions.Manage)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(524288)]
public sealed class FormPersonAttributesController(IFormPersonDataService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FormPersonAttributesResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await service.ListAttributesAsync(cancellationToken)).ToActionResult(this);

    [HttpPost]
    [HasPermission(BusinessFormsPermissions.PersonDataManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormPersonAttributeResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateFormPersonAttributeHttpRequest request, CancellationToken cancellationToken) =>
        (await service.CreateAttributeAsync(new(request.Key, request.Name, request.Type, request.SourceCatalogId), cancellationToken)).ToActionResult(this);

    [HttpPut("{id:guid}")]
    [HasPermission(BusinessFormsPermissions.PersonDataManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormPersonAttributeResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateFormPersonAttributeHttpRequest request, CancellationToken cancellationToken) =>
        (await service.UpdateAttributeAsync(id, new(request.ExpectedRevision, request.Name, request.Type, request.SourceCatalogId, request.Position, request.IsActive), cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}/options")]
    [ProducesResponseType<PagedResult<FormPersonAttributeOptionResponse>>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ListOptions([FromRoute] Guid id, [FromQuery] FormPersonAttributeOptionsQuery query, CancellationToken cancellationToken) =>
        (await service.ListOptionsAsync(id, new() { Page = query.Page, PageSize = query.PageSize, Sort = query.Sort,
            Search = query.Search, DisplayCulture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName }, cancellationToken)).ToActionResult(this);
}
