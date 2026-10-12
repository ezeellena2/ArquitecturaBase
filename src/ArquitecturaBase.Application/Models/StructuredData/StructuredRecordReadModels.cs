namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredRecordReadResult(bool CanReadRecords,
    IReadOnlyDictionary<Guid, StructuredRecordReadReply> Replies);

public sealed record StructuredRecordReadReply(Guid SourceReceiptId, Guid RecordId, string Number,
    Guid DefinitionId, Guid VersionId, long VersionNumber, string Name, IReadOnlyList<StructuredRecordReadValue> Values);

public sealed record StructuredRecordReadValue(Guid FieldId, string Key, string Type, string Label, object? Value);
public sealed record StructuredRecordChoiceValue(string ChoiceId, string Label,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? CatalogId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? CatalogName = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Detail = null);
