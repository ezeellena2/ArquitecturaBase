namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

/// <summary>A Home photo or PDF (Stage 3): the browser's bytes, recognized by their content, the person's file name and the culture of the request.</summary>
public sealed record SubmitAiAssistantDocumentRequest(Guid RequestId, string Culture, byte[] Content, string FileName)
{
    public override string ToString() => "SubmitAiAssistantDocumentRequest { Content = [REDACTED] }";
}

/// <summary>Where the reading of a Home document is; the document and how readable it was only once it completed, for this same person.</summary>
public sealed record AiAssistantDocumentResponse(Guid RequestId, string State, AiAssistantReadDocumentResponse? Document, AiAssistantFailure? Failure,
    int PollingAfterSeconds)
{
    public override string ToString() => "AiAssistantDocumentResponse { Document = [REDACTED] }";
}

/// <summary>
/// The document a completed reading read: its file name, its kind (pdf, jpeg or png), its pages (a photo is one), its size, whether it could
/// be read (readable, partial or unreadable) and, when not whole, why (blurry, dark, cut, covered or other).
/// </summary>
public sealed record AiAssistantReadDocumentResponse(string Name, string Kind, int Pages, long Size, string Readability, string? Problem)
{
    public override string ToString() => "AiAssistantReadDocumentResponse { Name = [REDACTED] }";
}
