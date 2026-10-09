using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;
internal sealed class AesGcmAiSecretProtector(AiKeyRing ring) : IAiSecretProtector
{
    public string Protect(AiProvider provider, string apiKey)
    {
        var keyId = ring.ActiveKeyId;
        var key = ring.Find(keyId) ?? throw new InvalidOperationException("The active encryption key is unavailable.");
        var plain = Encoding.UTF8.GetBytes(apiKey);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var cipher = new byte[plain.Length];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(keyId, provider));
            return string.Join('.', "1", keyId, Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(cipher));
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
    public string? Unprotect(AiProvider provider, string protectedApiKey)
    {
        byte[]? key = null;
        byte[]? plain = null;
        try
        {
            var parts = protectedApiKey.Split('.');
            if (parts.Length != 5 || parts[0] != "1") return null;
            key = ring.Find(parts[1]);
            if (key is null) return null;
            var cipher = Convert.FromBase64String(parts[4]);
            plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(Convert.FromBase64String(parts[2]), cipher, Convert.FromBase64String(parts[3]), plain, AssociatedData(parts[1], provider));
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException) { return null; }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }
    private byte[] AssociatedData(string keyId, AiProvider provider) => Encoding.UTF8.GetBytes($"ArquitecturaBase.AI|1|{ring.InstallationId}|{keyId}|api-key|{provider}");
}
