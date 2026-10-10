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
        var result = await service.AcceptAsync(id, Map(request), cancellationToken);
        if (result.IsSuccess) Response.Headers.Location = result.Value.RecoveryPath;
        return result.ToAcceptedResult(this);
    }
    [HttpGet("{requestId:guid}")]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormPublicationStatusResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid id, [FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await statusService.GetAsync(id, requestId, cancellationToken)).ToActionResult(this);

    private static AcceptFormPublicationRequest Map(AcceptFormPublicationHttpRequest request) => new(request.RequestId,
        request.ExpectedRevision, request.CheckToken, request.Name, request.Prefix,
        request.Draft is { } draft ? new(draft.Format, draft.Culture, draft.SubmitLabel,
            draft.Fields?.Select(field => field is null ? null! : new FormFieldInput(field.LocalId, field.FieldId, field.ReplacesFieldId,
                field.Type, field.Label, field.Help, field.Required, field.MaxLength,
                field.Options?.Select(option => option is null ? null! : new FormChoiceInput(option.LocalId, option.ChoiceId, option.Label)).ToArray())).ToArray()!) : null!);
}
