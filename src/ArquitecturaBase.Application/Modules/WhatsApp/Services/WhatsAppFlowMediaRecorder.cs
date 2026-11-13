using System.Security.Cryptography;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFlowMediaRecorder(IWhatsAppMediaClient client, TimeProvider timeProvider, IPrivateFileStore files)
{
    /// <summary>
    /// Task 9 of the documents: a file the send carries is read from this module's own private copy, never from Meta, bounded by its size
    /// and checked against its SHA-256: a copy that is gone is unavailable and one that changed is not taken.
    /// </summary>
    public async Task<Result<WhatsAppMediaContent>> ReadServerFileAsync(WhatsAppFlowServerFile file, CancellationToken cancellationToken)
    {
        if (!files.IsAvailable) return FileErrors.Unavailable;
        await using var stored = await files.OpenReadAsync(file.StorageKey, cancellationToken);
        if (stored is null) return FileErrors.Unavailable;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var count = await stored.ReadAsync(chunk, cancellationToken);
            if (count == 0) break;
            if (buffer.Length + count > file.Size) return FileErrors.HashMismatch;
            buffer.Write(chunk, 0, count);
        }
        var content = buffer.ToArray();
        return content.LongLength == file.Size && Convert.ToHexStringLower(SHA256.HashData(content)) == file.Sha256
            ? new WhatsAppMediaContent(content) : FileErrors.HashMismatch;
    }

    public async Task WithinDeadlineAsync(Func<CancellationToken, Task> capture, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90), timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        await capture(linked.Token);
    }

    public Task<Result<WhatsAppMediaContent>> DownloadAsync(string mediaId, string contentType, string sha256, CancellationToken cancellationToken) =>
        client.DownloadAsync(mediaId, contentType, sha256, WhatsAppMediaContent.MaximumBytes, cancellationToken);
}
