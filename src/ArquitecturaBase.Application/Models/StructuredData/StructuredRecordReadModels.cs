namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredRecordReadResult(bool CanReadRecords,
    IReadOnlyDictionary<Guid, StructuredRecordReadReply> Replies);

public sealed record StructuredRecordReadReply(Guid SourceReceiptId, Guid RecordId, string Number,
    Guid DefinitionId, Guid VersionId, long VersionNumber, string Name, IReadOnlyList<StructuredRecordReadValue> Values)
{
    /// <summary>«Replaced» when a correction replaced the response, «Voided» when it was voided; null while it stands as it arrived.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Amendment { get; init; }
}

public sealed record StructuredRecordReadValue(Guid FieldId, string Key, string Type, string Label, object? Value);
/// <summary>An item of a block of rows: every question of its block, in order, with a null value where the item left it empty.</summary>
public sealed record StructuredRecordReadRow(IReadOnlyList<StructuredRecordReadValue> Values);
public sealed record StructuredRecordChoiceValue(string ChoiceId, string Label,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? CatalogId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? CatalogName = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Value = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ValueType = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<StructuredRecordCatalogFieldValue>? Fields = null)
{ public override string ToString() => "StructuredRecordChoiceValue { Content = [REDACTED] }"; }

public sealed record StructuredRecordFileValue(Guid FileId, string Name, string ContentType, long Size, bool HasThumbnail)
{ public override string ToString() => "StructuredRecordFileValue { Content = [REDACTED] }"; }

public sealed record StructuredRecordCatalogFieldValue(Guid FieldId, string Label, string Type, string Role, string Value,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Text = null)
{ public override string ToString() => "StructuredRecordCatalogFieldValue { Content = [REDACTED] }"; }
