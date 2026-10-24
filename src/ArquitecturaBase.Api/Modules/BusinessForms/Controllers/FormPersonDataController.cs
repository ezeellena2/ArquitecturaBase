using System.Globalization;
using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;

[ApiController]
[Route("api/forms/person-data")]
[Tags("Form person data")]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(524288)]
public sealed class FormPersonDataController(IFormPersonDataService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(BusinessFormsPermissions.PersonDataManage)]
    [HasPermission(Permissions.Users.Read)]
    [ProducesResponseType<FormPersonDataPageResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] FormPersonDataQuery query, CancellationToken cancellationToken) =>
        (await service.ListPeopleAsync(new() { Page = query.Page, PageSize = query.PageSize, Sort = query.Sort,
            Search = query.Search, DisplayCulture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName }, cancellationToken)).ToActionResult(this);

    [HttpGet("{userId:guid}")]
    [HasPermission(Permissions.Users.Manage)]
    [ProducesResponseType<FormPersonDataResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid userId, CancellationToken cancellationToken) =>
        (await service.GetDataAsync(userId, cancellationToken)).ToActionResult(this);

    [HttpPut("{userId:guid}")]
    [HasPermission(Permissions.Users.Manage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormPersonDataResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid userId, [FromBody] UpdateFormPersonDataHttpRequest request, CancellationToken cancellationToken) =>
        (await service.SaveDataAsync(userId, new(request.ExpectedRevision, request.Values), cancellationToken)).ToActionResult(this);
}
