namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredRecordReadResult(bool CanReadRecords,
    IReadOnlyDictionary<Guid, StructuredRecordReadReply> Replies);

public sealed record StructuredRecordReadReply(Guid SourceReceiptId, Guid RecordId, string Number,
    Guid DefinitionId, Guid VersionId, long VersionNumber, string Name, IReadOnlyList<StructuredRecordReadValue> Values);

public sealed record StructuredRecordReadValue(Guid FieldId, string Key, string Type, string Label, object? Value);
public sealed record StructuredRecordChoiceValue(string ChoiceId, string Label);
