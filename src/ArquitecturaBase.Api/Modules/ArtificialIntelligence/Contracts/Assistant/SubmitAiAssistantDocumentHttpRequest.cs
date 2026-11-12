using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts.Assistant;

/// <summary>A Home photo or PDF in multipart (Stage 3): the request that names it and the browser's file, recognized by its bytes.</summary>
public sealed record SubmitAiAssistantDocumentHttpRequest(Guid RequestId, [Required] IFormFile File)
{
    public override string ToString() => "SubmitAiAssistantDocumentHttpRequest { File = [REDACTED] }";
}
