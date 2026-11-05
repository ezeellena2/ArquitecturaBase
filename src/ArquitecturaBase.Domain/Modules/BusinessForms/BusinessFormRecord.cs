using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public enum FormRecordState { Received, PendingApproval, Approved, Rejected, Voided }

/// <summary>
/// A confirmed response. Its values never change: a correction is another record with the same number that replaces it
/// (<see cref="Correct"/>), and voiding only changes its state (<see cref="Void"/>). Both keep who, when and why.
/// </summary>
public sealed class BusinessFormRecord : Entity, IAuditable
{
    public const int MaxReasonLength = 500;
    private BusinessFormRecord() { }
    public Guid DefinitionId { get; private set; }
    public Guid VersionId { get; private set; }
    public Guid? RuleSetId { get; private set; }
    public FormRecordState State { get; private set; } = FormRecordState.Received;
    public Guid? OriginRecordId { get; private set; }
    public Guid? OriginSuggestionId { get; private set; }
    public string? CaptureOriginJson { get; private set; }
    public string? PresentationCulture { get; private set; }
    /// <summary>Loaded in the app by its own author, not received through a channel.</summary>
    public bool EnteredInApp { get; private set; }
    public long Number { get; private set; }
    public Guid ActorUserId { get; private set; }
    public Guid SourceReceiptId { get; private set; }
    public DateTime SubmittedAtUtc { get; private set; }
    public string ValuesJson { get; private set; } = "{}";
    /// <summary>The record this correction replaces; null for a response as it arrived.</summary>
    public Guid? ReplacesRecordId { get; private set; }
    /// <summary>The correction that replaced this record; a replaced record is no longer current.</summary>
    public Guid? ReplacedByRecordId { get; private set; }
    /// <summary>The receipt of the first version, which keeps the channel and who answered across corrections.</summary>
    public Guid? OriginalReceiptId { get; private set; }
    public Guid? CorrectedByUserId { get; private set; }
    public string? CorrectionReason { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public DateTime? VoidedAtUtc { get; private set; }
    public string? VoidReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    /// <summary>The receipt whose channel says how the response arrived: the first version's.</summary>
    public Guid OriginReceiptId => OriginalReceiptId ?? SourceReceiptId;
    /// <summary>Only the current version of a response that was not voided is corrected or voided.</summary>
    public bool CanBeAmended => ReplacedByRecordId is null && State != FormRecordState.Voided;

    public static BusinessFormRecord Create(Guid definitionId, Guid versionId, long number, Guid actorUserId,
        Guid sourceReceiptId, DateTime submittedAtUtc, string valuesJson, Guid? ruleSetId = null, string? presentationCulture = null,
        bool enteredInApp = false)
    {
        foreach (var id in new[] { definitionId, versionId, actorUserId, sourceReceiptId }) ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        if (ruleSetId == Guid.Empty) throw new ArgumentException("The rule revision must be a nonempty identifier.", nameof(ruleSetId));
        if (presentationCulture is not (null or "es" or "en"))
            throw new ArgumentException("The presentation culture must be supported.", nameof(presentationCulture));
        if (submittedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Submission time must be UTC.", nameof(submittedAtUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(valuesJson);
        return new() { DefinitionId = definitionId, VersionId = versionId, Number = number, ActorUserId = actorUserId,
            SourceReceiptId = sourceReceiptId, SubmittedAtUtc = submittedAtUtc, ValuesJson = valuesJson, RuleSetId = ruleSetId, PresentationCulture = presentationCulture,
            EnteredInApp = enteredInApp };
    }
    public void SetCaptureOrigin(Guid? originRecordId, Guid? originSuggestionId, string captureOriginJson)
    {
        if (CaptureOriginJson is not null) throw new InvalidOperationException("A captured form origin is immutable.");
        ArgumentException.ThrowIfNullOrWhiteSpace(captureOriginJson);
        if (originRecordId.HasValue != originSuggestionId.HasValue || originRecordId == Guid.Empty || originSuggestionId == Guid.Empty)
            throw new ArgumentException("Suggestion origin identifiers must form a nonempty pair.", nameof(originRecordId));
        OriginRecordId = originRecordId;
        OriginSuggestionId = originSuggestionId;
        CaptureOriginJson = captureOriginJson;
    }
    public Result MarkPendingApproval()
    {
        if (State != FormRecordState.Received) return FormRecordErrors.StateConflict;
        State = FormRecordState.PendingApproval;
        return Result.Success();
    }
    public Result CompleteApproval(bool approved)
    {
        if (State != FormRecordState.PendingApproval) return FormRecordErrors.StateConflict;
        State = approved ? FormRecordState.Approved : FormRecordState.Rejected;
        return Result.Success();
    }

    /// <summary>
    /// The correction that replaces this response: the same number, who answered, reception and channel, with new values
    /// that went through the capture again. It starts received; its rules run as for any arrival.
    /// </summary>
    public Result<BusinessFormRecord> Correct(Guid versionId, Guid sourceReceiptId, string valuesJson, Guid? ruleSetId,
        string? presentationCulture, Guid correctedByUserId, string? reason)
    {
        if (!CanBeAmended) return FormRecordErrors.AmendUnavailable;
        var normalized = NormalizeReason(reason);
        if (normalized.IsFailure) return normalized.Error;
        ArgumentOutOfRangeException.ThrowIfEqual(correctedByUserId, Guid.Empty);
        var correction = Create(DefinitionId, versionId, Number, ActorUserId, sourceReceiptId, SubmittedAtUtc, valuesJson,
            ruleSetId, presentationCulture, EnteredInApp);
        correction.ReplacesRecordId = Id;
        correction.OriginalReceiptId = OriginReceiptId;
        correction.CorrectedByUserId = correctedByUserId;
        correction.CorrectionReason = normalized.Value;
        ReplacedByRecordId = correction.Id;
        return correction;
    }

    /// <summary>The response stops counting but keeps its number, history and what it already produced; it is not undone.</summary>
    public Result Void(Guid actorUserId, string? reason, DateTime nowUtc)
    {
        if (!CanBeAmended) return FormRecordErrors.AmendUnavailable;
        var normalized = NormalizeReason(reason);
        if (normalized.IsFailure) return normalized.Error;
        ArgumentOutOfRangeException.ThrowIfEqual(actorUserId, Guid.Empty);
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("The void time must be UTC.", nameof(nowUtc));
        State = FormRecordState.Voided;
        VoidedByUserId = actorUserId;
        VoidedAtUtc = nowUtc;
        VoidReason = normalized.Value;
        return Result.Success();
    }

    /// <summary>A correction or a void says why: visible text of up to <see cref="MaxReasonLength"/> characters.</summary>
    public static Result<string> NormalizeReason(string? reason)
    {
        var normalized = reason?.Trim();
        if (string.IsNullOrEmpty(normalized)) return FormRecordErrors.ReasonRequired;
        if (normalized.Length > MaxReasonLength || !StorableText.IsStorable(normalized)) return FormRecordErrors.ReasonInvalid;
        return normalized;
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
    public int Position { get; private set; }

    public static BusinessFormRecordValue Create(Guid recordId, Guid definitionId, Guid fieldId, string type,
        string? textValue = null, decimal? decimalValue = null, DateOnly? dateValue = null,
        bool? booleanValue = null, Guid? choiceId = null, int position = 0)
    {
        foreach (var id in new[] { recordId, definitionId, fieldId }) ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, 20);
        var count = (textValue is not null ? 1 : 0) + (decimalValue.HasValue ? 1 : 0) + (dateValue.HasValue ? 1 : 0)
            + (booleanValue.HasValue ? 1 : 0) + (choiceId.HasValue ? 1 : 0);
        if (count != 1) throw new ArgumentException("A record value must contain exactly one typed value.");
        if (textValue is not null && !StorableText.IsStorable(textValue.AsSpan()))
            throw new ArgumentException("Record text must be storable.", nameof(textValue));
        return new() { RecordId = recordId, DefinitionId = definitionId, FieldId = fieldId, Type = type,
            TextValue = textValue, DecimalValue = decimalValue, DateValue = dateValue, BooleanValue = booleanValue, ChoiceId = choiceId, Position = position };
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
    public Guid? RejectedFieldId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public static BusinessFormCaptureReceipt Complete(Guid sourceReceiptId, Guid definitionId, Guid versionId,
        string fingerprint, Guid recordId, long number, DateTime nowUtc) => new()
    {
        SourceReceiptId = sourceReceiptId, DefinitionId = definitionId, VersionId = versionId, Fingerprint = fingerprint,
        RecordId = recordId, Number = number, State = "Completed", CreatedAtUtc = nowUtc,
    };
    public static BusinessFormCaptureReceipt Reject(Guid sourceReceiptId, Guid definitionId, Guid versionId,
        string fingerprint, string errorCode, DateTime nowUtc, Guid? rejectedFieldId = null) => new()
    {
        SourceReceiptId = sourceReceiptId, DefinitionId = definitionId, VersionId = versionId, Fingerprint = fingerprint,
        State = "Rejected", ErrorCode = errorCode, RejectedFieldId = rejectedFieldId, CreatedAtUtc = nowUtc,
    };
    public override string ToString() => $"BusinessFormCaptureReceipt {{ State = {State}, RecordId = {RecordId} }}";
}
