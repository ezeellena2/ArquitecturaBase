using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
[ApiController]
[Route("api/forms/draft-changes")]
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.Read)]
[EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
public sealed class FormDraftChangesController(IFormDraftService service) : ControllerBase
{
    [HttpGet("{requestId:guid}")]
    [ProducesResponseType<FormRecoveredChangeHttpResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Get([FromRoute] Guid requestId, CancellationToken cancellationToken)
    {
        var result = await service.GetChangeAsync(requestId, cancellationToken);
        if (result.IsFailure) return result.ToActionResult(this);
        var recovered = result.Value;
        var commit = recovered.Result;
        var path = Url.Action(nameof(Get), new { requestId = commit.RequestId })
            ?? throw new InvalidOperationException("The draft change route could not be generated.");
        var response = new FormRecoveredChangeHttpResponse(recovered.RequestId, recovered.Kind, recovered.Status, recovered.CommittedAtUtc,
            recovered.CurrentRevision, new(commit.RequestId, commit.Definition, commit.IdentityMap,
                new(path, commit.Recovery.ResultExpiresAtUtc)));
        return ArquitecturaBase.Domain.Results.Result.Success(response).ToActionResult(this);
    }
}
