namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredRecordCaptureRequest(Guid SourceReceiptId, Guid DefinitionId, Guid VersionId,
    Guid ActorUserId, DateTime SubmittedAtUtc, IReadOnlyDictionary<string, string> Answers, IReadOnlyList<StructuredFormCatalogOffer>? Catalogs = null, IReadOnlyList<StructuredRecordFileInput>? Files = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] StructuredRecordCaptureOrigin? Origin = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PresentationCulture { get; init; }

    public override string ToString() => "StructuredRecordCaptureRequest { Content = [REDACTED] }";
}

// RejectedRow is the item of a block of the rejected question, from 0; null outside a block.
public sealed record StructuredRecordCaptureResponse(string State, Guid? RecordId, long? Number, string? ErrorCode, Guid? RejectedFieldId = null,
    int? RejectedRow = null);

public sealed record StructuredRecordFileInput(Guid FieldId, int Position, string SourceId, string Type, string Name,
    string ContentType, string Sha256,
    [property: System.Text.Json.Serialization.JsonIgnore] byte[]? Content = null)
{
    public override string ToString() => "StructuredRecordFileInput { Content = [REDACTED] }";
}
