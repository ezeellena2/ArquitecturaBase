using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Models.Assistant;

/// <summary>
/// A spreadsheet attached to an interpretation: the descriptor of its private copy (server-owned, never accepted from HTTP or
/// model arguments) and its first sheet as the owner prepared it. The engine never opens the copy: the model and the capabilities
/// read the prepared sheet.
/// </summary>
public sealed record AssistantAttachmentInput(string StorageKey, string Name, string ContentType, long Size, string Sha256,
    DateTime ExpiresAtUtc, StructuredSpreadsheet Spreadsheet)
{
    /// <summary>As much as the owner prepares, of any kind: one limit in every layer.</summary>
    public const long MaximumBytes = StructuredAttachmentLimits.MaximumBytes;
    public override string ToString() => "AssistantAttachmentInput { Content = [REDACTED] }";
}

/// <summary>
/// The private copy of a turn's spreadsheet, its own or inherited, as the engine sealed it: its key, the person's file name, its
/// media type, size and SHA-256, for the owner to open the copy and check it is the same file.
/// </summary>
public sealed record AssistantAttachmentDescriptor(string StorageKey, string Name, string ContentType, long Size, string Sha256)
{
    public override string ToString() => "AssistantAttachmentDescriptor { Content = [REDACTED] }";
}
