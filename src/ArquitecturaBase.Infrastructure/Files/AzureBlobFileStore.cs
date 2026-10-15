using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Files;

internal sealed class AzureBlobFileStore : IPrivateFileStore
{
    private readonly BlobContainerClient? container;
    public bool IsAvailable => container is not null;

    public AzureBlobFileStore(IOptions<FileStorageOptions> options)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;
        var clientOptions = new BlobClientOptions();
        // Idempotent reads/writes with deterministic keys: SDK retries suffice. Never log names, bytes or keys.
        clientOptions.Retry.MaxRetries = 2;
        clientOptions.Retry.Delay = TimeSpan.FromMilliseconds(250);
        clientOptions.Retry.MaxDelay = TimeSpan.FromSeconds(2);
        clientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(30);
        clientOptions.Diagnostics.IsLoggingEnabled = false;
        clientOptions.Diagnostics.IsDistributedTracingEnabled = false;
        var service = settings.CredentialMode == "ConnectionString"
            ? new BlobServiceClient(settings.ConnectionString, clientOptions)
            : new BlobServiceClient(new Uri(settings.ServiceUri), Credential(settings), clientOptions);
        container = service.GetBlobContainerClient(settings.ContentContainer);
    }

    public async Task WriteAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        await GetContainer().GetBlobClient(key).UploadAsync(content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var response = await GetContainer().GetBlobClient(key).DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        await GetContainer().GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    private BlobContainerClient GetContainer() => container ?? throw new InvalidOperationException("Private file storage is not configured.");

    private static TokenCredential Credential(FileStorageOptions settings) => settings.CredentialMode switch
    {
        "AzureCli" => new AzureCliCredential(new AzureCliCredentialOptions { TenantId = settings.TenantId }),
        "VisualStudio" => new VisualStudioCredential(new VisualStudioCredentialOptions { TenantId = settings.TenantId }),
        "ManagedIdentity" => new ManagedIdentityCredential(string.IsNullOrEmpty(settings.ManagedIdentityClientId)
            ? ManagedIdentityId.SystemAssigned : ManagedIdentityId.FromUserAssignedClientId(settings.ManagedIdentityClientId)),
        _ => throw new InvalidOperationException("An explicit file storage credential mode is required."),
    };
}
