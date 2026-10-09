using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;
internal sealed class AiKeyRing(IOptions<AiSecretProtectionOptions> options)
{
    public string ActiveKeyId => options.Value.ActiveKeyId;
    public string InstallationId => options.Value.InstallationId;
    public byte[]? Find(string keyId) => options.Value.Keys.TryGetValue(keyId, out var value) ? Convert.FromBase64String(value) : null;
    public byte[] FingerprintKey(string keyId)
        => DeriveFingerprintKey(keyId, "ai-change-fingerprint:v1");
    public byte[] BusinessInformationFingerprintKey(string keyId)
        => DeriveFingerprintKey(keyId, "ai-business-information-fingerprint:v1");
    public byte[] AssistantContentKey(string keyId) => DeriveFingerprintKey(keyId, "assistant-content:v1");
    public byte[] AssistantFingerprintKey(string keyId) => DeriveFingerprintKey(keyId, "assistant-request-fingerprint:v1");
    private byte[] DeriveFingerprintKey(string keyId, string purpose)
    {
        var key = Find(keyId) ?? throw new InvalidOperationException("The fingerprint encryption key is unavailable. Restore the installation key ring.");
        try { return HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, Encoding.UTF8.GetBytes(InstallationId), Encoding.UTF8.GetBytes(purpose)); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
