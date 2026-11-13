using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts.Documents;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;

/// <summary>
/// «Armar formulario con la IA» (DOC6) with «Ver documentos recibidos», «Revisar documentos» and «Diseñar formularios» (its answers carry inbox
/// documents): asking for a form from one unclassified document
/// or a group of similar ones (202, then read like a dictation; an engine that refuses gives 403 and one that fails 500) and «Crear borrador»
/// from its proposal as the reviewer edited it (201, once per request). Closed bodies up to 512 KiB, no-store and the forms limits.
/// </summary>
[ApiController]
[Route("api/forms")]
[Tags("Form documents")]
[HasPermission(BusinessFormsPermissions.DocumentsRead)]
[HasPermission(BusinessFormsPermissions.DocumentsReview)]
[HasPermission(BusinessFormsPermissions.Read)]
[HasPermission(BusinessFormsPermissions.Manage)]
[RequestSizeLimit(524288)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormDocumentProposalsController(IFormDocumentAuthoringService proposals, IFormDocumentDraftService drafts) : ControllerBase
{
    [HttpPost("documents/form-proposals")]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormDocumentProposalResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status403Forbidden)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    [ProducesProblem(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Ask([FromBody] RequestFormDocumentProposalHttpRequest request, CancellationToken cancellationToken) =>
        (await proposals.RequestAsync(new(request.RequestId, request.DocumentId, request.TopicId), cancellationToken)).ToAcceptedResult(this);

    [HttpGet("documents/form-proposals/{requestId:guid}")]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormDocumentProposalResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status403Forbidden)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get([FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await proposals.GetAsync(requestId, cancellationToken)).ToActionResult(this);

    [HttpPost("definitions/document-proposals")]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormDraftAssistantProposalResponse>(StatusCodes.Status201Created)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status403Forbidden)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateDraft([FromBody] CreateFormDocumentDraftHttpRequest request, CancellationToken cancellationToken) =>
        (await drafts.CreateAsync(new(request.RequestId, request.ProposalId, request.Name, request.Prefix, request.SubmitLabel, request.Recognition,
            request.Questions?.Select(question => question is null ? null
                : new CreateFormDocumentDraftQuestion(question.Label, question.Type, question.Required, question.Options, question.CatalogId)).ToArray()),
            cancellationToken)).ToCreatedResult(this, nameof(Get), _ => new { requestId = request.RequestId });
}
