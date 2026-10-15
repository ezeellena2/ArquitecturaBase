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
}
