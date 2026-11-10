using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// A spreadsheet as Meta describes it in the webhook: the media id, the type, the SHA-256 in lowercase hex (null when it is not one)
/// and the person's file name. Anything may be missing: the message decides whether it can be downloaded, and keeps the name anyway.
/// </summary>
public sealed record WhatsAppWebhookSpreadsheetDescriptor(string? MediaId, string? ContentType, string? Sha256, string? FileName)
{
    public override string ToString() => "WhatsAppWebhookSpreadsheetDescriptor { Metadata = [REDACTED] }";
}

/// <summary>A received spreadsheet with its conversation: what downloading it needs and who may have it downloaded.</summary>
public sealed record WhatsAppSpreadsheetSourceRow
{
    public Guid MessageId { get; init; }
    public Guid ContactId { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public WhatsAppConversationMode Mode { get; init; }
    public Guid? LinkedUserId { get; init; }
    public long LinkRevision { get; init; }
    public WhatsAppSpreadsheetState State { get; init; }
    public string? MediaId { get; init; }
    public string? ContentType { get; init; }
    public string? Sha256 { get; init; }
    public string? FileName { get; init; }
    public override string ToString() => "WhatsAppSpreadsheetSourceRow { Metadata = [REDACTED] }";
}
