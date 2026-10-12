using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public enum WhatsAppFlowCaptureState { Pending, Completed, Rejected, Unrecognized }

public sealed class WhatsAppFlowSession : Entity, IAuditable
{
    private WhatsAppFlowSession() { }
    public Guid ContactId { get; private set; }
    public long LinkRevision { get; private set; }
    public Guid UserId { get; private set; }
    public Guid OperatorUserId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid VersionId { get; private set; }
    public int VersionNumber { get; private set; }
    public Guid BindingId { get; private set; }
    public string Culture { get; private set; } = "";
    public string NameJson { get; private set; } = "";
    public string Prefix { get; private set; } = "";
    public string TokenHash { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public byte[] ProtectedPayload { get; private set; } = [];
    public DateTime ExpiresAtUtc { get; private set; }
    public WhatsAppFlowCaptureState CaptureState { get; private set; }
    public Guid? CompletedRecordId { get; private set; }
    public string? CompletedNumber { get; private set; }
    public string? CaptureErrorCode { get; private set; }
    public string? RejectedQuestion { get; private set; }
    public string? ResponseWaMessageId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static WhatsAppFlowSession Create(Guid contactId, long linkRevision, Guid userId, Guid actor, Guid definitionId,
        Guid versionId, int versionNumber, Guid bindingId, string culture, string nameJson, string prefix,
        string tokenHash, string requestFingerprint, DateTime expiresAtUtc)
    {
        foreach (var id in new[] { contactId, userId, actor, definitionId, versionId, bindingId })
            ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(linkRevision);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (prefix.Length > 12) throw new ArgumentOutOfRangeException(nameof(prefix));
        if (culture is not ("es" or "en") || expiresAtUtc.Kind != DateTimeKind.Utc
            || !IsHash(tokenHash) || !IsHash(requestFingerprint))
            throw new ArgumentException("The Flow session must have a fixed culture, UTC expiry and complete fingerprints.");
        return new() { ContactId = contactId, LinkRevision = linkRevision, UserId = userId, OperatorUserId = actor, DefinitionId = definitionId,
            VersionId = versionId, VersionNumber = versionNumber, BindingId = bindingId, Culture = culture,
            NameJson = nameJson, Prefix = prefix, TokenHash = tokenHash, RequestFingerprint = requestFingerprint, ExpiresAtUtc = expiresAtUtc };
    }
    public void SealPayload(byte[] payload)
    {
        if (ProtectedPayload.Length != 0 || payload.Length == 0)
            throw new InvalidOperationException("The private Flow session payload is sealed exactly once.");
        ProtectedPayload = payload.ToArray();
    }
    public bool CompleteCapture(string responseWaMessageId, WhatsAppFlowCaptureState state,
        Guid? recordId, string? number, string? errorCode, string? rejectedQuestion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseWaMessageId);
        if (CaptureState != WhatsAppFlowCaptureState.Pending)
        {
            if (CaptureState == state && CompletedRecordId == recordId
                && CompletedNumber == number && CaptureErrorCode == errorCode) return true;
            return false;
        }
        if (DirectionInvalid(state, recordId, number))
            throw new InvalidOperationException("A Flow session capture is terminal and may be recorded only once.");
        ResponseWaMessageId = responseWaMessageId;
        CaptureState = state;
        CompletedRecordId = recordId;
        CompletedNumber = number;
        CaptureErrorCode = errorCode;
        RejectedQuestion = state == WhatsAppFlowCaptureState.Rejected ? rejectedQuestion : null;
        return true;
    }
    private static bool DirectionInvalid(WhatsAppFlowCaptureState state, Guid? recordId, string? number) =>
        !Enum.IsDefined(state) || (state == WhatsAppFlowCaptureState.Completed) != (recordId.HasValue && number is not null);
    private static bool IsHash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);
    public override string ToString() => "WhatsAppFlowSession { Content = [REDACTED] }";
}
