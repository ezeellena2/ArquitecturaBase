namespace ArquitecturaBase.Application.Models.StructuredData;
public sealed record StructuredRecordOrigin(Guid SourceReceiptId, string ChannelId, Guid? SenderUserId, string? SenderName,
    DateTime SentAtUtc, Guid? ConversationId, string? MaskedAddress, string SenderKind)
{
    /// <summary>
    /// When the channel asked for the location before the form, and when it arrived or the person went on without it (W1-A). Only the
    /// owner's history uses them; the origin keeps its shape.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore] public DateTime? LocationRequestedAtUtc { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public DateTime? LocationResolvedAtUtc { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public bool LocationSkipped { get; init; }
    public override string ToString() => "StructuredRecordOrigin { Content = [REDACTED] }";
}
