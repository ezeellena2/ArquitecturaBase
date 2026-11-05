using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts.Assistant;

/// <summary>A Home recording in multipart (IA4): the request that names it and the browser's file, recognized by its bytes.</summary>
public sealed record SubmitAiAssistantAudioHttpRequest(Guid RequestId, [Required] IFormFile File)
{
    public override string ToString() => "SubmitAiAssistantAudioHttpRequest { File = [REDACTED] }";
}
