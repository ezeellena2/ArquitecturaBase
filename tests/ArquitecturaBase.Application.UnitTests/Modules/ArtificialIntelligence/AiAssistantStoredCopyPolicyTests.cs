using System.Security.Cryptography;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// I9: a kept copy is read back in blocks with an incremental SHA-256, never whole, and still tells the file received from another one.
public sealed class AiAssistantStoredCopyPolicyTests
{
    private static readonly byte[] Received = "Código,Cliente\nA1,Cliente ficticio\nA2,Otro cliente ficticio\n"u8.ToArray();
    private static readonly DateTime Expires = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_copy_read_back_a_few_bytes_at_a_time_is_the_file_received()
    {
        await using var stored = new TrickleStream(Received, 3);

        Assert.True(await AiAssistantStoredCopyPolicy.MatchesAsync(stored, Received.LongLength, Sha(Received), Ct));
    }

    [Fact]
    public async Task A_copy_of_the_same_size_with_other_bytes_a_longer_one_or_a_shorter_one_is_not_the_file_received()
    {
        byte[] other = [.. Received];
        other[^2] ^= 0x01;

        foreach (var copy in new[] { other, [.. Received, (byte)'x'], Received[..^1] })
        {
            await using var stored = new TrickleStream(copy, 5);
            Assert.False(await AiAssistantStoredCopyPolicy.MatchesAsync(stored, Received.LongLength, Sha(Received), Ct));
        }
    }

    [Fact]
    public async Task Each_home_copy_already_kept_with_other_bytes_of_the_same_size_is_a_conflict()
    {
        byte[] other = [.. Received];
        other[0] ^= 0x01;
        var requestId = Guid.NewGuid();
        var files = new MemoryPrivateFileStore();

        var spreadsheet = new AssistantAttachmentInput(AiAssistantHomeAttachmentPolicy.Key(requestId, StructuredSpreadsheetKind.Csv),
            "clientes.csv", "text/csv", Received.LongLength, Sha(Received), Expires, null);
        await KeepAsync(files, spreadsheet.StorageKey, other);
        var attachments = new AiAssistantHomeAttachmentPolicy(files, null!, null!, null!);
        Assert.Equal(AiAssistantErrors.RequestReused, (await attachments.StoreAsync(spreadsheet, Received, Ct)).Error);

        var document = new AssistantDocumentInput(AiAssistantHomeDocumentPolicy.Key(requestId, "application/pdf"), "comprobante.pdf", "application/pdf",
            Received.LongLength, Sha(Received), 1, Expires);
        await KeepAsync(files, document.StorageKey, other);
        var documents = new AiAssistantHomeDocumentPolicy(files, null!, null!, null!);
        Assert.Equal(AiAssistantErrors.RequestReused, (await documents.StoreAsync(document, Received, Ct)).Error);

        await KeepAsync(files, AiAssistantHomeAudioPolicy.Key(requestId, "audio/webm"), other);
        var recordings = new AiAssistantHomeAudioPolicy(files, null!, null!, new FakeTimeProvider());
        Assert.Equal(AiAssistantErrors.RequestReused, (await recordings.StoreAsync(requestId, Received, "audio/webm", Expires, Ct)).Error);
        Assert.Equal(3, files.WriteCount);
    }

    private static async Task KeepAsync(MemoryPrivateFileStore files, string key, byte[] content)
    {
        await using var stream = new MemoryStream(content, false);
        Assert.True(await files.WriteOnceAsync(key, stream, "application/octet-stream", Ct));
    }

    private static string Sha(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    // A copy that gives back at most a few bytes per read and does not say its length, as a remote store may.
    private sealed class TrickleStream(byte[] content, int most) : MemoryStream(content, false)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(most, buffer.Length)]);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(most, buffer.Length)], cancellationToken);
    }
}
