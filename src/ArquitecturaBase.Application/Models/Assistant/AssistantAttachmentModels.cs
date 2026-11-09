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
    /// <summary>As much as a catalog import reads.</summary>
    public const long MaximumBytes = 10 * 1024 * 1024;
    public override string ToString() => "AssistantAttachmentInput { Content = [REDACTED] }";
}
