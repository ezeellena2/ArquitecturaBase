using System.Security.Cryptography;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;
/// <summary>
/// I17: the control of a key is an HMAC-SHA256 of a fixed label under a key derived by HKDF from that key, with the InstallationId as
/// salt and its own purpose, like the other derived keys. Another InstallationId or other bytes under the same keyId give another
/// value, and the value does not reveal the key.
/// </summary>
internal sealed class HmacAiKeyRingControl(AiKeyRing ring) : IAiKeyRingControl
{
    private static readonly byte[] Label = "ArquitecturaBase AI key ring control"u8.ToArray();
    public string ActiveKeyId => ring.ActiveKeyId;
    public IReadOnlyDictionary<string, string> CreateControls() => ring.KeyIds.ToDictionary(keyId => keyId, Control, StringComparer.Ordinal);
    private string Control(string keyId)
    {
        var key = ring.ControlKey(keyId);
        try { return Convert.ToBase64String(HMACSHA256.HashData(key, Label)); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
