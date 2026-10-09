using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;
internal sealed class HmacAiChangeFingerprint(AiKeyRing ring) : IAiChangeFingerprint
{
    public string ActiveKeyId => ring.ActiveKeyId;
    public string Create(UpdateAiConnectionRequest request, string keyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = ring.FingerprintKey(keyId);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new object?[] { request.Provider.ToString(), request.Model.Trim(), request.CredentialChange.ToString(),
            request.ApiKey, request.Enabled, request.ExpectedRevision, request.RequestId });
        try { return Convert.ToBase64String(HMACSHA256.HashData(key, payload)); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(payload); }
    }
    public bool Matches(UpdateAiConnectionRequest request, string keyId, string fingerprint)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(Create(request, keyId)), Convert.FromBase64String(fingerprint)); }
        catch (FormatException) { return false; }
    }
}
