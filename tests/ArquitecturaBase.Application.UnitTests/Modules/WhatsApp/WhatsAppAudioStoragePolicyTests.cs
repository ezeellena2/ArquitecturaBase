using System.Security.Cryptography;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Domain.Files;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppAudioStoragePolicyTests
{
    private static byte[] Audio()
    {
        var bytes = new byte[47]; "OggS"u8.CopyTo(bytes); bytes[26] = 1; bytes[27] = 19;
        "OpusHead"u8.CopyTo(bytes.AsSpan(28)); bytes[36] = 1; bytes[37] = 1;
        return bytes;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    [Fact]
    public async Task Stores_verified_opus_once_under_a_private_server_key()
    {
        var store = new MemoryPrivateFileStore(); var policy = new WhatsAppAudioStoragePolicy(store); var id = Guid.NewGuid(); var bytes = Audio();
        var result = await policy.SaveAsync(id, bytes, Hash(bytes), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.Equal(47, result.Value.Size); Assert.Equal(WhatsAppAudioStoragePolicy.Key(id), result.Value.Key); Assert.Equal(1, store.WriteCount);
    }
    [Fact]
    public async Task Reconciles_an_object_left_before_metadata_commit_without_overwriting_it()
    {
        var store = new MemoryPrivateFileStore(); var policy = new WhatsAppAudioStoragePolicy(store); var id = Guid.NewGuid(); var bytes = Audio();
        await store.WriteOnceAsync(WhatsAppAudioStoragePolicy.Key(id), new MemoryStream(bytes), "audio/ogg", TestContext.Current.CancellationToken);
        var result = await policy.FindAsync(id, Hash(bytes), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.NotNull(result.Value); Assert.Equal(bytes.Length, result.Value.Size); Assert.Equal(1, store.WriteCount);
    }
    [Fact]
    public async Task Rejects_an_existing_object_with_different_bytes_without_replacing_it()
    {
        var store = new MemoryPrivateFileStore(); var policy = new WhatsAppAudioStoragePolicy(store); var id = Guid.NewGuid(); var old = Audio(); old[^1] = 3;
        await store.WriteOnceAsync(WhatsAppAudioStoragePolicy.Key(id), new MemoryStream(old), "audio/ogg", TestContext.Current.CancellationToken);
        var result = await policy.SaveAsync(id, Audio(), Hash(Audio()), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure); Assert.Equal(FileErrors.HashMismatch, result.Error); Assert.Equal(1, store.WriteCount);
    }
    [Fact]
    public async Task Rejects_hash_mismatch_before_writing()
    {
        var store = new MemoryPrivateFileStore(); var result = await new WhatsAppAudioStoragePolicy(store).SaveAsync(Guid.NewGuid(), Audio(), new string('a',64), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure); Assert.Equal(FileErrors.HashMismatch, result.Error); Assert.Equal(0, store.WriteCount);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(46)] [InlineData(25000001)]
    public async Task Rejects_incomplete_or_oversized_bytes(int size)
    {
        var bytes = new byte[size]; var store = new MemoryPrivateFileStore(); var result = await new WhatsAppAudioStoragePolicy(store).SaveAsync(Guid.NewGuid(), bytes, Hash(bytes), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure); Assert.Equal(size > 25000000 ? FileErrors.TooLarge : FileErrors.InvalidType, result.Error); Assert.Equal(0, store.WriteCount);
    }
    [Fact]
    public async Task Rejects_non_opus_ogg_before_writing()
    {
        var bytes = Audio(); bytes[28] = 0; var store = new MemoryPrivateFileStore(); var result = await new WhatsAppAudioStoragePolicy(store).SaveAsync(Guid.NewGuid(), bytes, Hash(bytes), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure); Assert.Equal(FileErrors.InvalidType, result.Error); Assert.Equal(0, store.WriteCount);
    }
    [Fact]
    public async Task Missing_object_is_distinct_from_a_corrupt_object()
    {
        var result = await new WhatsAppAudioStoragePolicy(new MemoryPrivateFileStore()).FindAsync(Guid.NewGuid(), Hash(Audio()), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.Null(result.Value);
    }
    [Fact]
    public async Task Disabled_storage_is_not_a_missing_object()
    {
        var result = await new WhatsAppAudioStoragePolicy(new MemoryPrivateFileStore(false)).FindAsync(Guid.NewGuid(), Hash(Audio()), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure); Assert.Equal(FileErrors.Unavailable, result.Error);
    }
    [Fact]
    public async Task Repeated_save_preserves_the_first_object()
    {
        var store = new MemoryPrivateFileStore(); var policy = new WhatsAppAudioStoragePolicy(store); var id=Guid.NewGuid(); var bytes=Audio();
        Assert.True((await policy.SaveAsync(id,bytes,Hash(bytes),TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await policy.SaveAsync(id,bytes,Hash(bytes),TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(1,store.WriteCount);
    }
}
