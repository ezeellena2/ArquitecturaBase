using System.Globalization;
using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts.Assistant;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Controllers;

[ApiController]
[Route("api/ai/assistant")]
[Tags("AI assistant")]
[HasPermission(ArtificialIntelligencePermissions.AssistantUse)]
[EnableRateLimiting(ArtificialIntelligenceApiRegistration.ReadPolicy)]
[RequestSizeLimit(16384)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiAssistantController(IAiAssistantService service, IAiAssistantAudioService audio, IAiAssistantFileTurnService files,
    IAiAssistantDocumentService documents, ISystemSettingsService settings) : ControllerBase
{
    // A Home recording (IA4) plus its multipart envelope.
    private const long MaximumAudioRequestBytes = SubmitAiAssistantAudioRequest.MaximumBytes + 64 * 1024;
    // A spreadsheet (Stage 2) or a Word (Stage 3), as much as the owner prepares, plus the turn's fields and its multipart envelope.
    private const long MaximumFileTurnRequestBytes = AssistantAttachmentInput.MaximumBytes + 64 * 1024;
    // A photo or a PDF (Stage 3), as much as the owner prepares, plus its multipart envelope.
    private const long MaximumDocumentRequestBytes = AssistantAttachmentInput.MaximumBytes + 64 * 1024;

    [HttpGet("bootstrap")]
    [ProducesResponseType<AiAssistantBootstrapResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Bootstrap(CancellationToken cancellationToken) =>
        (await service.BootstrapAsync(cancellationToken)).ToActionResult(this);

    [HttpGet("conversations")]
    [ProducesResponseType<PagedResult<AiConversationSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListConversations([FromQuery] ListAiConversationsQuery query, CancellationToken cancellationToken) =>
        (await service.ListConversationsAsync(new ListAiConversationsRequest
        {
            Page = query.Page ?? PagedRequest.DefaultPage,
            PageSize = query.PageSize ?? (await settings.GetPresentationAsync(cancellationToken)).Value.DefaultPageSize,
            Sort = query.Sort,
        }, cancellationToken)).ToActionResult(this);

    [HttpGet("conversations/{conversationId:guid}")]
    [ProducesResponseType<AiConversationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConversation([FromRoute] Guid conversationId, CancellationToken cancellationToken) =>
        (await service.GetConversationAsync(conversationId, cancellationToken)).ToActionResult(this);

    [HttpGet("conversations/{conversationId:guid}/turns")]
    [ProducesResponseType<PagedResult<AiAssistantTurnResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTurns([FromRoute] Guid conversationId, [FromQuery] ListAiAssistantTurnsQuery query, CancellationToken cancellationToken) =>
        (await service.ListTurnsAsync(conversationId, new ListAiAssistantTurnsRequest
        {
            Page = query.Page ?? PagedRequest.DefaultPage,
            PageSize = query.PageSize ?? (await settings.GetPresentationAsync(cancellationToken)).Value.DefaultPageSize,
            Sort = query.Sort,
        }, cancellationToken)).ToActionResult(this);

    [HttpPost("turns")]
    [EnableRateLimiting(ArtificialIntelligenceApiRegistration.AssistantSubmitPolicy)]
    [ProducesResponseType<AiAssistantTurnAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit([FromBody] SubmitAiAssistantTurnHttpRequest request, CancellationToken cancellationToken) =>
        (await service.SubmitAsync(new(request.RequestId, request.ConversationId, request.ExpectedRevision,
            request.ExpectedConnectionRevision, request.Question, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
            { AudioRequestId = request.AudioRequestId, DocumentRequestId = request.DocumentRequestId }, cancellationToken)).ToAcceptedResult(this);

    [HttpPost("file-turns")]
    [EnableRateLimiting(ArtificialIntelligenceApiRegistration.AssistantSubmitPolicy)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumFileTurnRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumFileTurnRequestBytes)]
    [ProducesResponseType<AiAssistantTurnAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitFile([FromForm] SubmitAiAssistantFileTurnHttpRequest request, CancellationToken cancellationToken)
    {
        await using var content = new MemoryStream();
        await request.File.CopyToAsync(content, cancellationToken);
        var turn = new SubmitAiAssistantTurnRequest(request.RequestId, request.ConversationId, request.ExpectedRevision,
            request.ExpectedConnectionRevision, request.Question ?? "", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
            { AudioRequestId = request.AudioRequestId };
        return (await files.SubmitAsync(new(turn, content.ToArray(), request.File.FileName), cancellationToken)).ToAcceptedResult(this);
    }

    [HttpPost("audio")]
    [EnableRateLimiting(ArtificialIntelligenceApiRegistration.AssistantSubmitPolicy)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumAudioRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumAudioRequestBytes)]
    [ProducesResponseType<AiAssistantAudioResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitAudio([FromForm] SubmitAiAssistantAudioHttpRequest request, CancellationToken cancellationToken)
    {
        await using var content = new MemoryStream();
        await request.File.CopyToAsync(content, cancellationToken);
        return (await audio.SubmitAsync(new(request.RequestId, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, content.ToArray()),
            cancellationToken)).ToAcceptedResult(this);
    }

    [HttpGet("audio/{requestId:guid}")]
    [ProducesResponseType<AiAssistantAudioResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAudio([FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await audio.GetAsync(requestId, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, cancellationToken)).ToActionResult(this);

    [HttpGet("audio/{requestId:guid}/file")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "audio/webm", "audio/mp4")]
    public async Task<IActionResult> OpenAudio([FromRoute] Guid requestId, CancellationToken cancellationToken)
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        var result = await audio.OpenAsync(requestId, cancellationToken);
        return result.IsFailure ? result.ToActionResult(this) : File(result.Value.Content, result.Value.ContentType);
    }

    [HttpPost("documents")]
    [EnableRateLimiting(ArtificialIntelligenceApiRegistration.AssistantSubmitPolicy)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumDocumentRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumDocumentRequestBytes)]
    [ProducesResponseType<AiAssistantDocumentResponse>(StatusCodes.Status202Accepted)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitDocument([FromForm] SubmitAiAssistantDocumentHttpRequest request, CancellationToken cancellationToken)
    {
        await using var content = new MemoryStream();
        await request.File.CopyToAsync(content, cancellationToken);
        return (await documents.SubmitAsync(new(request.RequestId, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, content.ToArray(),
            request.File.FileName), cancellationToken)).ToAcceptedResult(this);
    }

    [HttpGet("documents/{requestId:guid}")]
    [ProducesResponseType<AiAssistantDocumentResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDocument([FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await documents.GetAsync(requestId, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, cancellationToken)).ToActionResult(this);

    [HttpGet("turns/{requestId:guid}")]
    [ProducesResponseType<AiAssistantTurnResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTurn([FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await service.GetTurnAsync(requestId, cancellationToken)).ToActionResult(this);

    [HttpPost("turns/{requestId:guid}/cancel")]
    [EnableRateLimiting(ArtificialIntelligenceApiRegistration.WritePolicy)]
    [ProducesResponseType<AiAssistantTurnResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel([FromRoute] Guid requestId, CancellationToken cancellationToken) =>
        (await service.CancelAsync(new(requestId), cancellationToken)).ToActionResult(this);

    [HttpDelete("conversations/{conversationId:guid}")]
    [EnableRateLimiting(ArtificialIntelligenceApiRegistration.WritePolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveConversation([FromRoute] Guid conversationId, CancellationToken cancellationToken) =>
        (await service.RemoveConversationAsync(conversationId, cancellationToken)).ToActionResult(this);
}
