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
[Route("api/forms/definitions/{id:guid}/publication-checks")]
[Tags("Forms")]
[HasPermission(BusinessFormsPermissions.Read)]
[HasPermission(BusinessFormsPermissions.Publish)]
[EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
[RequestSizeLimit(524288)]
public sealed class FormPublicationChecksController(IBusinessFormPublicationService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<FormPublicationCheckResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Check([FromRoute] Guid id, [FromBody] CheckFormPublicationHttpRequest request, CancellationToken cancellationToken) =>
        (await service.CheckAsync(id, Map(request), cancellationToken)).ToActionResult(this);
    private static CheckFormPublicationRequest Map(CheckFormPublicationHttpRequest request) => new(request.ExpectedRevision, request.Name, request.Prefix,
        request.Draft is { } draft ? new(draft.Format, draft.Culture, draft.SubmitLabel,
            draft.Fields?.Select(field => field is null ? null! : new FormFieldInput(field.LocalId, field.FieldId, field.ReplacesFieldId,
                field.Type, field.Label, field.Help, field.Required, field.MaxLength,
                field.Options?.Select(option => option is null ? null! : new FormChoiceInput(option.LocalId, option.ChoiceId, option.Label)).ToArray(),
                field.Source is null ? null : new FormQuestionSource(field.Source.Catalog), field.Files is null ? null : new FormFileSettings(field.Files.MinFiles, field.Files.MaxFiles, field.Files.PhotoSource))).ToArray()!) : null!);
}
