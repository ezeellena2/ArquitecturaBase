using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;

internal sealed class AesGcmAiAssistantContentProtector(AiKeyRing ring) : IAiConversationProtector
{
    public string Protect(Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long payloadRevision, string plaintext)
    {
        var keyId = ring.ActiveKeyId;
        var key = ring.AssistantContentKey(keyId);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var cipher = new byte[plain.Length];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(keyId, ownerUserId, conversationId, turnId, purpose, payloadRevision));
            return string.Join('.', "1", keyId, Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(cipher));
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }

    public string? Unprotect(Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long payloadRevision, string ciphertext)
    {
        byte[]? key = null;
        byte[]? plain = null;
        try
        {
            var parts = ciphertext.Split('.');
            if (parts.Length != 5 || parts[0] != "1" || ring.Find(parts[1]) is not { } rawKey) return null;
            CryptographicOperations.ZeroMemory(rawKey);
            key = ring.AssistantContentKey(parts[1]);
            var cipher = Convert.FromBase64String(parts[4]);
            plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(Convert.FromBase64String(parts[2]), cipher, Convert.FromBase64String(parts[3]), plain,
                AssociatedData(parts[1], ownerUserId, conversationId, turnId, purpose, payloadRevision));
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException) { return null; }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    private byte[] AssociatedData(string keyId, Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long revision) =>
        JsonSerializer.SerializeToUtf8Bytes(new object[] { "assistant-content:v1", ring.InstallationId, keyId, ownerUserId, conversationId, turnId, purpose, revision });
}
