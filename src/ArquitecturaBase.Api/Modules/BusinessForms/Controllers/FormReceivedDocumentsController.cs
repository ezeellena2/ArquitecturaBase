using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts.Documents;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Controllers;

/// <summary>
/// «Documentos recibidos» (DOC5-A, decisions 14 to 16): «Ver documentos recibidos» reads the inbox, a document and its file (409 when its
/// copy cannot be opened); «Revisar documentos» discards, reopens, changes its form (202, then the extraction is read like a dictation; an
/// engine that refuses gives 403 and one that fails 500) and passes it to the form. The limits and the no-store are those of the responses
/// routes.
/// </summary>
[ApiController]
[Route("api/forms/documents")]
[Tags("Form documents")]
[HasPermission(BusinessFormsPermissions.DocumentsRead)]
[RequestSizeLimit(524288)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormReceivedDocumentsController(IFormReceivedDocumentService documents, IFormReceivedDocumentExtractionService extractions,
    IFormReceivedDocumentRecordService records) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormReceivedDocumentsPageResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] ListFormReceivedDocumentsQuery query, CancellationToken cancellationToken) =>
        (await documents.ListAsync(new()
        {
            Page = query.Page, PageSize = query.PageSize, Sort = query.Sort, Search = query.Search, DefinitionId = query.DefinitionId,
            SenderUserId = query.SenderUserId, ReceivedFromUtc = query.ReceivedFromUtc, ReceivedToUtc = query.ReceivedToUtc, State = query.State,
        }, cancellationToken)).ToActionResult(this);

    [HttpGet("counts")]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormReceivedDocumentCountsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Counts(CancellationToken cancellationToken) =>
        (await documents.CountAsync(cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormReceivedDocumentDetailResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([FromRoute] Guid id, CancellationToken cancellationToken) =>
        (await documents.GetAsync(id, cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}/file")]
    [EnableRateLimiting(BusinessFormsApiRegistration.FilesReadPolicy)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/octet-stream")]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> OpenFile([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        var result = await documents.OpenAsync(id, cancellationToken);
        return result.IsFailure ? result.ToActionResult(this) : File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost("{id:guid}/discard")]
    [HasPermission(BusinessFormsPermissions.DocumentsReview)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormReceivedDocumentDetailResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Discard([FromRoute] Guid id, [FromBody] ReviewFormReceivedDocumentHttpRequest request, CancellationToken cancellationToken) =>
        (await documents.DiscardAsync(id, new(request.ExpectedRevision), cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/reopen")]
    [HasPermission(BusinessFormsPermissions.DocumentsReview)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormReceivedDocumentDetailResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reopen([FromRoute] Guid id, [FromBody] ReviewFormReceivedDocumentHttpRequest request, CancellationToken cancellationToken) =>
        (await documents.ReopenAsync(id, new(request.ExpectedRevision), cancellationToken)).ToActionResult(this);

    [HttpPut("{id:guid}/form")]
    [HasPermission(BusinessFormsPermissions.DocumentsReview)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormReceivedDocumentExtractionResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status403Forbidden)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    [ProducesProblem(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ChangeForm([FromRoute] Guid id, [FromBody] ChangeFormReceivedDocumentFormHttpRequest request, CancellationToken cancellationToken) =>
        (await extractions.RequestAsync(id, new(request.RequestId, request.DefinitionId, request.ExpectedRevision), cancellationToken)).ToAcceptedResult(this);

    [HttpGet("{id:guid}/extraction")]
    [HasPermission(BusinessFormsPermissions.DocumentsReview)]
    [EnableRateLimiting(BusinessFormsApiRegistration.ReadPolicy)]
    [ProducesResponseType<FormReceivedDocumentExtractionResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status403Forbidden)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Extraction([FromRoute] Guid id, [FromQuery] FormReceivedDocumentExtractionQuery query, CancellationToken cancellationToken) =>
        (await extractions.GetAsync(id, query.RequestId, cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/records")]
    [HasPermission(BusinessFormsPermissions.DocumentsReview)]
    [EnableRateLimiting(BusinessFormsApiRegistration.WritePolicy)]
    [ProducesResponseType<FormReceivedDocumentRecordResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Pass([FromRoute] Guid id, [FromBody] PassFormReceivedDocumentHttpRequest request, CancellationToken cancellationToken) =>
        (await records.PassAsync(id, new(request.RequestId, request.VersionId, request.ExpectedRevision,
            request.Answers?.Select(FormEntryRequestMapping.Answer).ToArray()), cancellationToken)).ToActionResult(this);
}
