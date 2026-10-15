using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Files;
internal sealed class AzuriteFixture : IAsyncDisposable
{
    private readonly IContainer container = new ContainerBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
        .WithPortBinding(10000, true)
        .WithCommand("azurite-blob", "--blobHost", "0.0.0.0", "--skipApiVersionCheck")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(10000)).Build();
    public string ConnectionString => $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://{container.Hostname}:{container.GetMappedPublicPort(10000)}/devstoreaccount1;";
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await container.StartAsync(cancellationToken);
        await new BlobServiceClient(ConnectionString).GetBlobContainerClient("files-content").CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
    }
    public ValueTask DisposeAsync() => container.DisposeAsync();
}
