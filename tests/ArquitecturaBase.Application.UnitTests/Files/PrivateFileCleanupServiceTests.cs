using ArquitecturaBase.Application.Services.Files;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Domain.Files;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
namespace ArquitecturaBase.Application.UnitTests.Files;
public sealed class PrivateFileCleanupServiceTests
{
    [Fact]
    public async Task Only_old_unreferenced_objects_are_deleted_including_uncommitted_thumbnails()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var store = new MemoryPrivateFileStore();
        foreach (var key in new[] { "kept", "kept.thumb", "orphan", "orphan.thumb", "fresh" })
        {
            await using var bytes = new MemoryStream([1]);
            await store.WriteAsync(key, bytes, "image/jpeg", CancellationToken.None);
            store.Modified[key] = clock.GetUtcNow().UtcDateTime.AddHours(key == "fresh" ? -1 : -25);
        }
        var metadata = new MemoryStoredFileRepository();
        metadata.Add(StoredFile.Create(Guid.CreateVersion7(), "file.jpg", "image/jpeg", 1, new string('a', 64), "kept", "kept.thumb"));
        var service = new PrivateFileCleanupService(store, metadata, clock, NullLogger<PrivateFileCleanupService>.Instance);
        Assert.True((await service.PurgeAsync(CancellationToken.None)).IsSuccess);
        Assert.Equal(new[] { "fresh", "kept", "kept.thumb" }, store.Modified.Keys.Order().ToArray());
        Assert.True((await service.PurgeAsync(CancellationToken.None)).IsSuccess);
    }
}
