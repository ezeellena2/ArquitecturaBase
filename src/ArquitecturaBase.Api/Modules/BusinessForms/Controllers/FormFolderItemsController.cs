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

// CP2-A: «Carpetas…» of a form (with «Ver formularios», and «Administrar formularios» to save) and of a catalog (with «Ver catálogos»,
// and «Administrar catálogos» to save). Saving sends the whole set with the one it read; 409 if it changed in between.
[ApiController]
[Route("api/forms")]
[Tags("Form folders")]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(65536)]
public sealed class FormFolderItemsController(IFormFolderItemService service) : ControllerBase
{
    [HttpGet("definitions/{id:guid}/folders")]
    [HasPermission(BusinessFormsPermissions.Read)]
    [ProducesResponseType<FormItemFoldersResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForm([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await service.GetAsync(FormFolderItemKind.Form, id, cancellationToken)).ToActionResult(this);

    [HttpPut("definitions/{id:guid}/folders")]
    [HasPermission(BusinessFormsPermissions.Read)]
    [HasPermission(BusinessFormsPermissions.Manage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormItemFoldersResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetForm([FromRoute] Guid id, [FromBody] SetFormItemFoldersHttpRequest request, CancellationToken cancellationToken) =>
        (await service.SetAsync(FormFolderItemKind.Form, id, Map(request), cancellationToken)).ToActionResult(this);

    [HttpGet("catalogs/{id:guid}/folders")]
    [HasPermission(BusinessFormsPermissions.CatalogsRead)]
    [ProducesResponseType<FormItemFoldersResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCatalog([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await service.GetAsync(FormFolderItemKind.Catalog, id, cancellationToken)).ToActionResult(this);

    [HttpPut("catalogs/{id:guid}/folders")]
    [HasPermission(BusinessFormsPermissions.CatalogsRead)]
    [HasPermission(BusinessFormsPermissions.CatalogsManage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormItemFoldersResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetCatalog([FromRoute] Guid id, [FromBody] SetFormItemFoldersHttpRequest request, CancellationToken cancellationToken) =>
        (await service.SetAsync(FormFolderItemKind.Catalog, id, Map(request), cancellationToken)).ToActionResult(this);

    private static SetFormItemFoldersRequest Map(SetFormItemFoldersHttpRequest request) => new(request.ExpectedFolderIds, request.FolderIds);
}
