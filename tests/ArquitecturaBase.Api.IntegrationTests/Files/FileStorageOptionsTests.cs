using ArquitecturaBase.Infrastructure.Files;

namespace ArquitecturaBase.Api.IntegrationTests.Files;

public sealed class FileStorageOptionsTests
{
    [Fact]
    public void Disabled_storage_does_not_require_credentials() =>
        Assert.True(new FileStorageOptions().IsValid());

    [Fact]
    public void Enabled_storage_requires_a_private_container_and_explicit_credentials()
    {
        Assert.False(new FileStorageOptions { Enabled = true }.IsValid());
        Assert.True(Emulator().IsValid());
        Assert.False((Emulator() with { ContentContainer = "Public/Files" }).IsValid());
        Assert.False((Emulator() with { CredentialMode = "Default" }).IsValid());
        Assert.False((Emulator() with { ConnectionString = "" }).IsValid());
    }

    [Fact]
    public void Cloud_credentials_require_https_without_query_or_embedded_secrets()
    {
        var cloud = new FileStorageOptions { Enabled = true, ContentContainer = "files-content",
            CredentialMode = "AzureCli", ServiceUri = "https://example.blob.core.windows.net" };
        Assert.True(cloud.IsValid());
        Assert.False((cloud with { ServiceUri = "http://example.blob.core.windows.net" }).IsValid());
        Assert.False((cloud with { ServiceUri = "https://example.blob.core.windows.net?sig=secret" }).IsValid());
        Assert.False((cloud with { ServiceUri = "https://user:secret@example.blob.core.windows.net" }).IsValid());
        Assert.DoesNotContain("secret", (Emulator() with { ConnectionString = "secret" }).ToString());
    }

    private static FileStorageOptions Emulator() => new() { Enabled = true,
        ContentContainer = "files-content", CredentialMode = "ConnectionString", ConnectionString = "UseDevelopmentStorage=true" };
}
