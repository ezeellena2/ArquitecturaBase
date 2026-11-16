using ArquitecturaBase.Domain.Common;
namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
/// <summary>
/// I17: the control value of a key of the ring, written by the first copy that has it. A copy whose own value for the same key
/// differs has another ring (another InstallationId or other bytes under the same keyId) and is not ready. It reveals neither the key
/// nor anything it protects.
/// </summary>
public sealed class AiKeyRingControl : Entity
{
    private AiKeyRingControl() { }
    public string KeyId { get; private set; } = string.Empty;
    public string Control { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public static AiKeyRingControl Create(string keyId, string control, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(control) || createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A key ring control requires a key identifier, a value and a UTC timestamp.");
        return new AiKeyRingControl { KeyId = keyId, Control = control, CreatedAtUtc = createdAtUtc };
    }
    public override string ToString() => "AiKeyRingControl { control: [REDACTED] }";
}
