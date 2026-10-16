using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
namespace ArquitecturaBase.Application.UnitTests.Files;
public sealed class PrivateFileWriteOnceTests
{
    [Fact]
    public async Task An_existing_private_copy_is_preserved_when_a_second_writer_has_different_bytes()
    {
        var store = new MemoryPrivateFileStore();
        await using var original = new MemoryStream([1, 2]); await using var later = new MemoryStream([3, 4, 5]);
        Assert.True(await store.WriteOnceAsync("fixed-copy", original, "application/pdf", CancellationToken.None));
        Assert.False(await store.WriteOnceAsync("fixed-copy", later, "application/pdf", CancellationToken.None));
        await using var saved = await store.OpenReadAsync("fixed-copy", CancellationToken.None);
        Assert.NotNull(saved); using var bytes = new MemoryStream(); await saved.CopyToAsync(bytes, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 1, 2 }, bytes.ToArray()); Assert.Equal(1, store.WriteCount);
    }
    [Fact]
    public async Task Concurrent_private_writers_agree_on_one_created_copy()
    {
        var store = new MemoryPrivateFileStore(); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(bool Created, byte[] Bytes)> WriteAsync(byte[] bytes)
        {
            await start.Task; await using var content = new MemoryStream(bytes);
            return (await store.WriteOnceAsync("fixed-copy", content, "application/pdf", CancellationToken.None), bytes);
        }
        var writers = new[] { WriteAsync([1]), WriteAsync([2, 3]) }; start.SetResult();
        var results = await Task.WhenAll(writers); var winner = Assert.Single(results, result => result.Created);
        await using var saved = await store.OpenReadAsync("fixed-copy", CancellationToken.None);
        Assert.NotNull(saved); using var bytes = new MemoryStream(); await saved.CopyToAsync(bytes, TestContext.Current.CancellationToken);
        Assert.Equal(winner.Bytes, bytes.ToArray()); Assert.Equal(1, store.WriteCount);
    }
}
