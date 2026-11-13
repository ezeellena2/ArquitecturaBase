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
    /// <summary>
    /// The received document the response came from (DOC5-A) and its file's name, by an opaque id of its owner; omitted otherwise, so every
    /// other origin keeps its bytes.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public StructuredRecordOriginDocument? Document { get; init; }
    public override string ToString() => "StructuredRecordOrigin { Content = [REDACTED] }";
}
public sealed record StructuredRecordOriginDocument(Guid Id, string Name)
{
    /// <summary>VC1-A: the row of a spreadsheet the response came from, with when its spreadsheet arrived; omitted for a document.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public StructuredRecordOriginRow? Row { get; init; }
    public override string ToString() => "StructuredRecordOriginDocument { Content = [REDACTED] }";
}
/// <summary>A row of a spreadsheet: its sheet (only when the book has more than one), its number and when its spreadsheet arrived.</summary>
public sealed record StructuredRecordOriginRow(string? Sheet, int Number, DateTime ReceivedAtUtc);
