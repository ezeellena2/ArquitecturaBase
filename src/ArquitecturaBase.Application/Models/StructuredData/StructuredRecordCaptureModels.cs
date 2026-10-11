namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredRecordCaptureRequest(Guid SourceReceiptId, Guid DefinitionId, Guid VersionId,
    Guid ActorUserId, DateTime SubmittedAtUtc, IReadOnlyDictionary<string, string> Answers)
{
    public override string ToString() => "StructuredRecordCaptureRequest { Content = [REDACTED] }";
}

public sealed record StructuredRecordCaptureResponse(string State, Guid? RecordId, long? Number, string? ErrorCode);
