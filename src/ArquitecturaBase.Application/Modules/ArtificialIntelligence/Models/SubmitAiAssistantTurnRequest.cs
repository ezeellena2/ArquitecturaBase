using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

public sealed record SubmitAiAssistantTurnRequest(Guid RequestId, Guid? ConversationId, long? ExpectedRevision,
    long ExpectedConnectionRevision, string Question, string Culture)
{
    public string NormalizedQuestion => Question.Trim().Replace("\r\n", "\n", StringComparison.Ordinal);
    /// <summary>A dictated turn (IA4): the completed normalization whose transcript the server takes; the question is then optional.</summary>
    public Guid? AudioRequestId { get; init; }
    /// <summary>A spreadsheet or a Word the server prepared for the turn, never bound from HTTP; the question is then optional.</summary>
    public AssistantAttachmentInput? Attachment { get; init; }
    /// <summary>A turn with a photo or a PDF (Stage 3): the completed reading whose document the server attaches; the question is then optional.</summary>
    public Guid? DocumentRequestId { get; init; }
    public override string ToString() => "SubmitAiAssistantTurnRequest { question: [REDACTED] }";
}
