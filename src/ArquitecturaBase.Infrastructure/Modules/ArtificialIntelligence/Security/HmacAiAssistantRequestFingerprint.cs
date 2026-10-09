using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;

internal sealed class HmacAiAssistantRequestFingerprint(AiKeyRing ring) : IAiAssistantRequestFingerprint
{
    public string ActiveKeyId => ring.ActiveKeyId;
    public string Create(Guid ownerUserId, SubmitAiAssistantTurnRequest request, string keyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = ring.AssistantFingerprintKey(keyId);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new object?[] { ownerUserId, request.RequestId, request.ConversationId,
            request.ExpectedRevision, request.ExpectedConnectionRevision, request.NormalizedQuestion });
        try { return Convert.ToBase64String(HMACSHA256.HashData(key, payload)); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(payload); }
    }

    public bool Matches(Guid ownerUserId, SubmitAiAssistantTurnRequest request, string keyId, string fingerprint)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(Create(ownerUserId, request, keyId)), Convert.FromBase64String(fingerprint)); }
        catch (FormatException) { return false; }
    }
}
