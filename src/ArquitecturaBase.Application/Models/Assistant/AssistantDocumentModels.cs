using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Models.Assistant;

/// <summary>
/// A document the engine reads (ReadDocument): the descriptor of its private copy, server-owned and never accepted from HTTP or model
/// arguments. A PDF, a JPEG or a PNG up to <see cref="StructuredAttachmentLimits.MaximumBytes"/>, with 1 to
/// <see cref="StructuredAttachmentLimits.MaximumPages"/> pages (a photo is one), its SHA-256 in lowercase and the expiry of the copy.
/// </summary>
public sealed record AssistantDocumentInput(string StorageKey, string Name, string ContentType, long Size, string Sha256, int Pages,
    DateTime ExpiresAtUtc)
{
    public override string ToString() => "AssistantDocumentInput { Content = [REDACTED] }";
}
