using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class BusinessFormRecord : Entity, IAuditable
{
    private BusinessFormRecord() { }
    public Guid DefinitionId { get; private set; }
    public Guid VersionId { get; private set; }
    public long Number { get; private set; }
    public Guid ActorUserId { get; private set; }
    public Guid SourceReceiptId { get; private set; }
    public DateTime SubmittedAtUtc { get; private set; }
    public string ValuesJson { get; private set; } = "{}";
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static BusinessFormRecord Create(Guid definitionId, Guid versionId, long number, Guid actorUserId,
        Guid sourceReceiptId, DateTime submittedAtUtc, string valuesJson)
    {
        foreach (var id in new[] { definitionId, versionId, actorUserId, sourceReceiptId }) ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        if (submittedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Submission time must be UTC.", nameof(submittedAtUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(valuesJson);
        return new() { DefinitionId = definitionId, VersionId = versionId, Number = number, ActorUserId = actorUserId,
            SourceReceiptId = sourceReceiptId, SubmittedAtUtc = submittedAtUtc, ValuesJson = valuesJson };
    }
    public override string ToString() => $"BusinessFormRecord {{ Id = {Id}, Number = {Number}, Values = [REDACTED] }}";
}

public sealed class BusinessFormRecordValue : Entity
{
    private BusinessFormRecordValue() { }
    public Guid RecordId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid FieldId { get; private set; }
    public string Type { get; private set; } = "";
    public string? TextValue { get; private set; }
    public decimal? DecimalValue { get; private set; }
    public DateOnly? DateValue { get; private set; }
    public bool? BooleanValue { get; private set; }
    public Guid? ChoiceId { get; private set; }

    public static BusinessFormRecordValue Create(Guid recordId, Guid definitionId, Guid fieldId, string type,
        string? textValue = null, decimal? decimalValue = null, DateOnly? dateValue = null,
        bool? booleanValue = null, Guid? choiceId = null)
    {
        foreach (var id in new[] { recordId, definitionId, fieldId }) ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        var count = (textValue is not null ? 1 : 0) + (decimalValue.HasValue ? 1 : 0) + (dateValue.HasValue ? 1 : 0)
            + (booleanValue.HasValue ? 1 : 0) + (choiceId.HasValue ? 1 : 0);
        if (count != 1) throw new ArgumentException("A record value must contain exactly one typed value.");
        if (textValue is not null && !StorableText.IsStorable(textValue.AsSpan()))
            throw new ArgumentException("Record text must be storable.", nameof(textValue));
        return new() { RecordId = recordId, DefinitionId = definitionId, FieldId = fieldId, Type = type,
            TextValue = textValue, DecimalValue = decimalValue, DateValue = dateValue, BooleanValue = booleanValue, ChoiceId = choiceId };
    }
}

public sealed class BusinessFormCaptureReceipt : Entity
{
    private BusinessFormCaptureReceipt() { }
    public Guid SourceReceiptId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid VersionId { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public Guid? RecordId { get; private set; }
    public long? Number { get; private set; }
    public string State { get; private set; } = "";
    public string? ErrorCode { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public static BusinessFormCaptureReceipt Complete(Guid sourceReceiptId, Guid definitionId, Guid versionId,
        string fingerprint, Guid recordId, long number, DateTime nowUtc) => new()
    {
        SourceReceiptId = sourceReceiptId, DefinitionId = definitionId, VersionId = versionId, Fingerprint = fingerprint,
        RecordId = recordId, Number = number, State = "Completed", CreatedAtUtc = nowUtc,
    };
    public static BusinessFormCaptureReceipt Reject(Guid sourceReceiptId, Guid definitionId, Guid versionId,
        string fingerprint, string errorCode, DateTime nowUtc) => new()
    {
        SourceReceiptId = sourceReceiptId, DefinitionId = definitionId, VersionId = versionId, Fingerprint = fingerprint,
        State = "Rejected", ErrorCode = errorCode, CreatedAtUtc = nowUtc,
    };
    public override string ToString() => $"BusinessFormCaptureReceipt {{ State = {State}, RecordId = {RecordId} }}";
}
