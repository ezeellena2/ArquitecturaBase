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

// CP1-A, CP3-A and CM1 to CM6: the tree of a listing is read by whoever sees that listing (the service checks the one of «kind»);
// creating, renaming, moving, changing the icon of a menu, deleting and choosing the forms of a folder need to manage forms.
[ApiController]
[Route("api/forms/folders")]
[Tags("Form folders")]
[EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(65536)]
public sealed class FormFoldersController(IFormFolderService service) : ControllerBase
{
    [HttpGet]
    [HasAnyPermission(BusinessFormsPermissions.Read, BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.RecordsRead,
        BusinessFormsPermissions.ApprovalsDecide, BusinessFormsPermissions.RecordsCreate)]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormFolderTreeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? kind, CancellationToken cancellationToken) =>
        (await service.ListAsync(new(kind), cancellationToken)).ToActionResult(this);

    [HttpPost]
    [HasPermission(BusinessFormsPermissions.Manage)]
    [ProducesResponseType<FormFolderResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateFormFolderHttpRequest request, CancellationToken cancellationToken) =>
        (await service.CreateAsync(new(request.ParentId, request.Name, request.Icon), cancellationToken)).ToActionResult(this);

    [HttpPut("{id:guid}")]
    [HasPermission(BusinessFormsPermissions.Manage)]
    [ProducesResponseType<FormFolderResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateFormFolderHttpRequest request, CancellationToken cancellationToken) =>
        (await service.UpdateAsync(id, new(request.ExpectedRevision, request.Name, request.ParentId, request.Icon), cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}/deletion")]
    [HasPermission(BusinessFormsPermissions.Manage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormFolderDeletionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeletion([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await service.GetDeletionAsync(id, cancellationToken)).ToActionResult(this);

    [HttpDelete("{id:guid}")]
    [HasPermission(BusinessFormsPermissions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, [FromBody] DeleteFormFolderHttpRequest request, CancellationToken cancellationToken) =>
        (await service.DeleteAsync(id, new(request.ExpectedRevision), cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}/forms")]
    [HasPermission(BusinessFormsPermissions.Manage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormFolderFormsResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetForms([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await service.GetFormsAsync(id, cancellationToken)).ToActionResult(this);

    // Up to 1000 forms, twice with the ones that were read: more than the 64 KiB of the other bodies.
    [HttpPut("{id:guid}/forms")]
    [HasPermission(BusinessFormsPermissions.Manage)]
    [RequestSizeLimit(131072)]
    [ProducesResponseType<FormFolderFormsResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetForms([FromRoute] Guid id, [FromBody] SetFormFolderFormsHttpRequest request, CancellationToken cancellationToken) =>
        (await service.SetFormsAsync(id, new(request.ExpectedFormIds, request.FormIds), cancellationToken)).ToActionResult(this);
}
