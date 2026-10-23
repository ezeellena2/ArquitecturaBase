using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;

[ApiController]
[Route("api/forms/definitions/{id:guid}/publications")]
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.Read)]
[HasPermission(BusinessFormsPermissions.Publish)]
public sealed class FormPublicationsController(IFormPublicationAcceptanceService service, IFormPublicationStatusService statusService) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [RequestSizeLimit(524288)]
    [ProducesResponseType<FormPublicationReceiptResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Accept([FromRoute] Guid id, [FromBody] AcceptFormPublicationHttpRequest request, CancellationToken cancellationToken)
    {
        var result = await service.AcceptAsync(id, FormDraftRequestMapping.Map(request), cancellationToken);
        if (result.IsFailure) return result.ToAcceptedResult(this);

        var receipt = result.Value;
        var recoveryPath = Url.Action(nameof(Get), new { id, requestId = receipt.RequestId })
            ?? throw new InvalidOperationException("The publication status route could not be generated.");
        var response = new FormPublicationReceiptHttpResponse(receipt.RequestId, receipt.OperationId, receipt.DefinitionId,
            receipt.VersionId, receipt.VersionNumber, receipt.DraftRevision, receipt.State, receipt.AcceptedAtUtc, recoveryPath);
        return Result.Success(response).ToAcceptedResult(this);
    }
    [HttpGet("{requestId:guid}")]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormPublicationStatusResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid id, [FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await statusService.GetAsync(id, requestId, cancellationToken)).ToActionResult(this);
}
