namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

/// <summary>A Home recording (IA4): the browser's bytes, recognized by their content, and the culture of the request.</summary>
public sealed record SubmitAiAssistantAudioRequest(Guid RequestId, string Culture, byte[] Content)
{
    /// <summary>The same bound as a WhatsApp audio; the HTTP limit adds its multipart envelope.</summary>
    public const int MaximumBytes = 1_000_000;

    public override string ToString() => "SubmitAiAssistantAudioRequest { Content = [REDACTED] }";
}

/// <summary>Where the normalization of a recording is; the transcript only once it completed, for this same person.</summary>
public sealed record AiAssistantAudioResponse(Guid RequestId, string State, string? Transcript, AiAssistantFailure? Failure, int PollingAfterSeconds)
{
    public override string ToString() => "AiAssistantAudioResponse { Transcript = [REDACTED] }";
}

/// <summary>The private recording, to listen to it in the thread while its content lasts.</summary>
public sealed record AiAssistantAudioFileResponse(Stream Content, string ContentType)
{
    public override string ToString() => "AiAssistantAudioFileResponse { Content = [REDACTED] }";
}
