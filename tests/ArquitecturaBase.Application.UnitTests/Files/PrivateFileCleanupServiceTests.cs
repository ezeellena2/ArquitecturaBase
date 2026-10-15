using ArquitecturaBase.Application.Services.Files;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Domain.Files;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
namespace ArquitecturaBase.Application.UnitTests.Files;
public sealed class PrivateFileCleanupServiceTests
{
    [Fact]
    public async Task Cleanup_reads_metadata_in_bounded_batches_instead_of_once_per_object()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var store = new MemoryPrivateFileStore(); var metadata = new MemoryStoredFileRepository();
        for (var index = 0; index < 500; index++)
        {
            var key = "orphan-" + index;
            await using var content = new MemoryStream([1]);
            await store.WriteAsync(key, content, "application/pdf", CancellationToken.None);
            store.Modified[key] = clock.GetUtcNow().UtcDateTime.AddDays(-2);
        }
        var service = new PrivateFileCleanupService(store, metadata, clock, NullLogger<PrivateFileCleanupService>.Instance);
        Assert.True((await service.PurgeAsync(CancellationToken.None)).IsSuccess);
        Assert.Empty(store.Modified);
        Assert.Equal(4, metadata.KeyLookupCount);
    }
    [Fact]
    public async Task Only_old_unreferenced_objects_are_deleted_including_uncommitted_thumbnails()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var store = new MemoryPrivateFileStore();
        foreach (var key in new[] { "kept", "kept.thumb.jpg", "orphan", "orphan.thumb.jpg", "fresh" })
        {
            await using var bytes = new MemoryStream([1]);
            await store.WriteAsync(key, bytes, "image/jpeg", CancellationToken.None);
            store.Modified[key] = clock.GetUtcNow().UtcDateTime.AddHours(key == "fresh" ? -1 : -25);
        }
        var metadata = new MemoryStoredFileRepository();
        metadata.Add(StoredFile.Create(Guid.CreateVersion7(), "file.jpg", "image/jpeg", 1, new string('a', 64), "kept", "kept.thumb.jpg"));
        var service = new PrivateFileCleanupService(store, metadata, clock, NullLogger<PrivateFileCleanupService>.Instance);
        Assert.True((await service.PurgeAsync(CancellationToken.None)).IsSuccess);
        Assert.Equal(new[] { "fresh", "kept", "kept.thumb.jpg" }, store.Modified.Keys.Order().ToArray());
        Assert.True((await service.PurgeAsync(CancellationToken.None)).IsSuccess);
    }
}
