using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts.Assistant;

/// <summary>
/// A Home turn with a spreadsheet in multipart (Stage 2): the fields of a turn, with the written question optional, and the browser's
/// file, whose kind its owner reads from the bytes. The culture comes from the localization.
/// </summary>
public sealed record SubmitAiAssistantFileTurnHttpRequest(Guid RequestId, Guid? ConversationId, long? ExpectedRevision,
    long ExpectedConnectionRevision, [Required] IFormFile File, string? Question = null, Guid? AudioRequestId = null)
{
    public override string ToString() => "SubmitAiAssistantFileTurnHttpRequest { question: [REDACTED], File = [REDACTED] }";
}
