using ArquitecturaBase.Domain.Common;
namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
public sealed class AiBusinessInformationChange : Entity
{
    private AiBusinessInformationChange() { }
    public Guid RequestId { get; private set; }
    public Guid OperatorId { get; private set; }
    public string Fingerprint { get; private set; } = string.Empty;
    public string KeyId { get; private set; } = string.Empty;
    public long Revision { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public static AiBusinessInformationChange Create(Guid requestId, Guid operatorId, string fingerprint, string keyId, long revision, DateTime createdAtUtc)
    {
        if (requestId == Guid.Empty || operatorId == Guid.Empty || createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A change requires an operator, a request identifier and a UTC timestamp.");
        return new AiBusinessInformationChange { RequestId = requestId, OperatorId = operatorId, Fingerprint = fingerprint,
            KeyId = keyId, Revision = revision, CreatedAtUtc = createdAtUtc };
    }
    public override string ToString() => "AiBusinessInformationChange { fingerprint: [REDACTED] }";
}
