using System.Security.Cryptography;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppAudioStoragePolicy(IPrivateFileStore store)
{
    public const int MaximumBytes = 25000000;
    public const int MaximumNormalizationBytes = 1_000_000;
    public static string Key(Guid messageId) => $"whatsapp/audio/{messageId:N}.ogg";

    public async Task<Result<WhatsAppStoredAudio?>> FindAsync(Guid messageId, string sha256, CancellationToken ct)
    {
        if (!store.IsAvailable) return FileErrors.Unavailable;
        await using var stream = await store.OpenReadAsync(Key(messageId), ct);
        if (stream is null) return Result.Success<WhatsAppStoredAudio?>(null);
        var bytes = await ReadBoundedAsync(stream, ct);
        if (bytes.IsFailure) return bytes.Error;
        if (Validate(bytes.Value, sha256) is { } error) return error;
        return new WhatsAppStoredAudio(Key(messageId), bytes.Value.Length);
    }
    public async Task<Result<WhatsAppStoredAudio>> SaveAsync(Guid messageId, byte[] bytes, string sha256, CancellationToken ct)
    {
        if (!store.IsAvailable) return FileErrors.Unavailable;
        if (Validate(bytes, sha256) is { } error) return error;
        var existing = await FindAsync(messageId, sha256, ct);
        if (existing.IsFailure) return existing.Error;
        if (existing.Value is { } found) return found;
        await using var content = new MemoryStream(bytes, false);
        await store.WriteOnceAsync(Key(messageId), content, "audio/ogg", ct);
        // A concurrent host may have won WriteOnce. Re-read the preserved object, never assume it is ours.
        var verified = await FindAsync(messageId, sha256, ct);
        return verified.IsFailure ? verified.Error : verified.Value is { } value ? value : FileErrors.Unavailable;
    }
    public async Task<Result<Stream>> OpenAsync(Guid messageId, string sha256, CancellationToken ct)
    {
        if (!store.IsAvailable) return FileErrors.Unavailable;
        await using var stream = await store.OpenReadAsync(Key(messageId), ct);
        if (stream is null) return FileErrors.NotFound;
        var bytes = await ReadBoundedAsync(stream, ct);
        if (bytes.IsFailure) return bytes.Error;
        if (Validate(bytes.Value, sha256) is { } error) return error;
        return Result.Success<Stream>(new MemoryStream(bytes.Value, false));
    }
    private static async Task<Result<byte[]>> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream(); var chunk = new byte[81920];
        while (true)
        {
            var count = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, MaximumBytes + 1 - (int)buffer.Length)), ct);
            if (count == 0) return buffer.ToArray();
            buffer.Write(chunk, 0, count);
            if (buffer.Length > MaximumBytes) return FileErrors.TooLarge;
        }
    }
    private static Error? Validate(byte[] bytes, string hash)
    {
        if (bytes.Length > MaximumBytes) return FileErrors.TooLarge;
        if (!Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase)) return FileErrors.HashMismatch;
        if (bytes.Length < 47 || !bytes.AsSpan(0,4).SequenceEqual("OggS"u8) || bytes[4] != 0 || bytes[26] == 0) return FileErrors.InvalidType;
        var offset = 27 + bytes[26];
        if (bytes.Length < offset + 19 || bytes[27] < 19 || !bytes.AsSpan(offset,8).SequenceEqual("OpusHead"u8)
            || bytes[offset + 8] > 15 || bytes[offset + 9] is not (1 or 2) || bytes[offset + 18] != 0) return FileErrors.InvalidType;
        return null;
    }
}
internal sealed record WhatsAppStoredAudio(string Key, long Size)
{
    public override string ToString() => "WhatsAppStoredAudio { Metadata = [REDACTED] }";
}
