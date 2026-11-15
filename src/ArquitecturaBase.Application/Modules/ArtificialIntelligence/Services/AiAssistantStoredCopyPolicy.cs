using System.Security.Cryptography;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

// I9: whether a private copy read back is the file received, compared in blocks with an incremental SHA-256 and never whole in memory:
// the same size and the same hash. Home's spreadsheets, documents and recordings re-read their copy with it after WriteOnce.
internal static class AiAssistantStoredCopyPolicy
{
    private const int BlockBytes = 81920;

    public static async Task<bool> MatchesAsync(Stream stored, long size, string sha256, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var block = new byte[BlockBytes];
        long length = 0;
        for (int read; (read = await stored.ReadAsync(block, ct)) > 0;)
        {
            length += read;
            if (length > size) return false;
            hash.AppendData(block, 0, read);
        }
        return length == size && string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), sha256, StringComparison.Ordinal);
    }
}
