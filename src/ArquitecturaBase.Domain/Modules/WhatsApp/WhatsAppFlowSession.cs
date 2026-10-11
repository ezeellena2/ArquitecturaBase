using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public sealed class WhatsAppFlowSession : Entity, IAuditable
{
    private WhatsAppFlowSession() { }
    public Guid ContactId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid OperatorUserId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public Guid VersionId { get; private set; }
    public int VersionNumber { get; private set; }
    public Guid BindingId { get; private set; }
    public string Culture { get; private set; } = "";
    public string NameJson { get; private set; } = "";
    public string TokenHash { get; private set; } = "";
    public string RequestFingerprint { get; private set; } = "";
    public byte[] ProtectedPayload { get; private set; } = [];
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static WhatsAppFlowSession Create(Guid contactId, Guid userId, Guid actor, Guid definitionId,
        Guid versionId, int versionNumber, Guid bindingId, string culture, string nameJson,
        string tokenHash, string requestFingerprint, DateTime expiresAtUtc)
    {
        foreach (var id in new[] { contactId, userId, actor, definitionId, versionId, bindingId })
            ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameJson);
        if (culture is not ("es" or "en") || expiresAtUtc.Kind != DateTimeKind.Utc
            || !IsHash(tokenHash) || !IsHash(requestFingerprint))
            throw new ArgumentException("The Flow session must have a fixed culture, UTC expiry and complete fingerprints.");
        return new() { ContactId = contactId, UserId = userId, OperatorUserId = actor, DefinitionId = definitionId,
            VersionId = versionId, VersionNumber = versionNumber, BindingId = bindingId, Culture = culture,
            NameJson = nameJson, TokenHash = tokenHash, RequestFingerprint = requestFingerprint, ExpiresAtUtc = expiresAtUtc };
    }
    public void SealPayload(byte[] payload)
    {
        if (ProtectedPayload.Length != 0 || payload.Length == 0)
            throw new InvalidOperationException("The private Flow session payload is sealed exactly once.");
        ProtectedPayload = payload.ToArray();
    }
    private static bool IsHash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);
    public override string ToString() => "WhatsAppFlowSession { Content = [REDACTED] }";
}
