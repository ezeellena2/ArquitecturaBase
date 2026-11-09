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

// BI1-A: the library of standards. Reading it needs «Ver formularios»; «Agregar» creates an own draft and also needs «Administrar».
[ApiController]
[Route("api/forms/library")]
[Tags("Form library")]
[HasPermission(BusinessFormsPermissions.Read)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormLibraryController(IFormLibraryService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FormLibraryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await service.ListAsync(cancellationToken)).ToActionResult(this);

    [HttpPost("{key}/copies")]
    [HasPermission(BusinessFormsPermissions.Manage)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [RequestSizeLimit(65536)]
    [ProducesResponseType<FormDraftCommitHttpResponse>(StatusCodes.Status201Created)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Add([FromRoute] string key, [FromBody] AddFormStandardHttpRequest request, CancellationToken cancellationToken)
    {
        var result = await service.AddAsync(key, new AddFormStandardRequest(request.RequestId, request.Name, request.Prefix), cancellationToken);
        if (result.IsFailure) return result.ToActionResult(this);
        var location = Url.Action(nameof(FormDefinitionsController.Get), "FormDefinitions", new { id = result.Value.Definition.Id })
            ?? throw new InvalidOperationException("The form route could not be generated.");
        var recovery = Url.Action(nameof(FormDraftChangesController.Get), "FormDraftChanges", new { requestId = result.Value.RequestId })
            ?? throw new InvalidOperationException("The draft change route could not be generated.");
        return Created(location, new FormDraftCommitHttpResponse(result.Value.RequestId, result.Value.Definition, result.Value.IdentityMap,
            new(recovery, result.Value.Recovery.ResultExpiresAtUtc)));
    }
}
