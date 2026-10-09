using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public sealed class AiConnectionChange : Entity
{
    private AiConnectionChange() { }
    public Guid RequestId { get; private set; }
    public Guid OperatorId { get; private set; }
    public string Fingerprint { get; private set; } = string.Empty;
    public string KeyId { get; private set; } = string.Empty;
    public long Revision { get; private set; }
    public AiProvider Provider { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public bool HasCredential { get; private set; }
    public bool Enabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static AiConnectionChange Create(Guid requestId, Guid operatorId, string fingerprint, string keyId,
        AiConnection connection, DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (requestId == Guid.Empty || operatorId == Guid.Empty || createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A change requires an operator, a request identifier and a UTC timestamp.");
        return new AiConnectionChange
        {
            RequestId = requestId, OperatorId = operatorId, Fingerprint = fingerprint, KeyId = keyId,
            Revision = connection.Revision, Provider = connection.Provider, Model = connection.Model,
            HasCredential = connection.ProtectedApiKey is not null, Enabled = connection.Enabled, CreatedAtUtc = createdAtUtc,
        };
    }

    public override string ToString() => "AiConnectionChange { fingerprint: [REDACTED] }";
}
