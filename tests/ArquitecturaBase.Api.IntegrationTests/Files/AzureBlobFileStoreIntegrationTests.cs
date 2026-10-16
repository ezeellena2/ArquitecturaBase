using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Files;
using ArquitecturaBase.Infrastructure.Files;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Api.IntegrationTests.Files;
public sealed class AzureBlobFileStoreIntegrationTests
{
    [Fact]
    public async Task A_private_azurite_blob_can_be_overwritten_read_and_deleted_idempotently()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var emulator = new AzuriteFixture(); await emulator.StartAsync(ct);
        var container = new BlobServiceClient(emulator.ConnectionString).GetBlobContainerClient("files-content");
        Assert.Equal(PublicAccessType.None, (await container.GetAccessPolicyAsync(cancellationToken: ct)).Value.BlobPublicAccess);
        var store = new AzureBlobFileStore(Options.Create(new FileStorageOptions { Enabled = true, CredentialMode = "ConnectionString", ConnectionString = emulator.ConnectionString }));
        foreach (var bytes in new byte[][] { [1, 2], [3, 4, 5] })
        {
            await using var source = new MemoryStream(bytes); await store.WriteAsync("server/generated-key", source, "application/pdf", ct);
        }
        await using (var content = await store.OpenReadAsync("server/generated-key", ct))
        {
            Assert.NotNull(content);
            using var buffer = new MemoryStream(); await content.CopyToAsync(buffer, ct); Assert.Equal(new byte[] { 3, 4, 5 }, buffer.ToArray());
        }
        await store.DeleteAsync("server/generated-key", ct); await store.DeleteAsync("server/generated-key", ct);
        Assert.Null(await store.OpenReadAsync("server/generated-key", ct));
        Assert.False((await container.GetBlobClient("server/generated-key").ExistsAsync(ct)).Value);
    }
    [Fact]
    public async Task Conditional_private_uploads_from_two_stores_preserve_exactly_one_winning_copy()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var emulator = new AzuriteFixture(); await emulator.StartAsync(ct);
        var settings = Options.Create(new FileStorageOptions { Enabled = true, CredentialMode = "ConnectionString", ConnectionString = emulator.ConnectionString });
        var first = new AzureBlobFileStore(settings); var second = new AzureBlobFileStore(settings);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(bool Created, byte[] Bytes)> WriteAsync(AzureBlobFileStore store, byte[] bytes)
        {
            await start.Task.WaitAsync(ct); await using var content = new MemoryStream(bytes);
            return (await store.WriteOnceAsync("fixed/pdf-copy", content, "application/pdf", ct), bytes);
        }
        var attempts = new[] { WriteAsync(first, [1, 2]), WriteAsync(second, [3, 4, 5]) }; start.SetResult();
        var results = await Task.WhenAll(attempts); var winner = Assert.Single(results, result => result.Created);
        await using var late = new MemoryStream([9]);
        Assert.False(await second.WriteOnceAsync("fixed/pdf-copy", late, "text/plain", ct));
        await using var saved = await first.OpenReadAsync("fixed/pdf-copy", ct);
        Assert.NotNull(saved); using var buffer = new MemoryStream(); await saved.CopyToAsync(buffer, ct);
        Assert.Equal(winner.Bytes, buffer.ToArray());
        var blob = new BlobServiceClient(emulator.ConnectionString).GetBlobContainerClient("files-content").GetBlobClient("fixed/pdf-copy");
        Assert.Equal("application/pdf", (await blob.GetPropertiesAsync(cancellationToken: ct)).Value.ContentType);
    }
}
