using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormFileServiceTests
{
    [Fact]
    public async Task Read_permission_and_membership_are_checked_before_opening_any_content()
    {
        var setup = await CreateAsync(true);
        setup.Permissions.Permissions.Clear();
        Assert.True((await setup.Service.OpenAsync(setup.RecordId, setup.File.Id, false, false, CancellationToken.None)).IsFailure);
        Assert.Equal(0, setup.Store.ReadCount);
        setup.Permissions.Permissions[setup.User.UserId!.Value] = [BusinessFormsPermissions.RecordsRead];
        Assert.Equal(FileErrors.NotFound, (await setup.Service.OpenAsync(Guid.CreateVersion7(), setup.File.Id, false, false, CancellationToken.None)).Error);
        Assert.Equal(0, setup.Store.ReadCount);
    }
    [Fact]
    public async Task Revocation_during_the_storage_read_closes_the_stream_without_returning_bytes()
    {
        var setup = await CreateAsync(true);
        setup.Store.BeforeRead = () => setup.Permissions.Permissions.Clear();
        Assert.True((await setup.Service.OpenAsync(setup.RecordId, setup.File.Id, false, false, CancellationToken.None)).IsFailure);
        Assert.False(setup.Store.LastOpenedStream!.CanRead);
    }
    [Theory]
    [InlineData(true, false, null)]
    [InlineData(true, true, "private.png")]
    [InlineData(false, false, "private.png")]
    public async Task Only_photos_can_be_inline_and_documents_always_download(bool photo, bool download, string? name)
    {
        var setup = await CreateAsync(photo);
        var result = await setup.Service.OpenAsync(setup.RecordId, setup.File.Id, false, download, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(name, result.Value.DownloadName);
        await result.Value.Content.DisposeAsync();
    }
    [Fact]
    public async Task Missing_content_returns_not_found_even_when_metadata_exists()
    {
        var setup = await CreateAsync(false);
        await setup.Store.DeleteAsync(setup.File.StorageKey, CancellationToken.None);
        var result = await setup.Service.OpenAsync(setup.RecordId, setup.File.Id, false, false, CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotFound, result.Error);
    }
    private static async Task<(FormFileService Service, MemoryPrivateFileStore Store, FakePermissionService Permissions, FakeCurrentUser User, Guid RecordId, StoredFile File)> CreateAsync(bool photo)
    {
        var user = new FakeCurrentUser { UserId = Guid.CreateVersion7() };
        var permissions = new FakePermissionService(); permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead];
        var file = StoredFile.Create(user.UserId.Value, "private.png", "image/png", 3, new string('a', 64), "original", photo ? "thumb" : null);
        var metadata = new MemoryStoredFileRepository(); metadata.Add(file);
        var store = new MemoryPrivateFileStore();
        await using var content = new MemoryStream([1, 2, 3]);
        await store.WriteAsync(file.StorageKey, content, file.ContentType, CancellationToken.None);
        var recordId = Guid.CreateVersion7();
        var service = new FormFileService(new FakeFormFileReader(recordId, file.Id, photo), metadata, store, user, permissions, NullLogger<FormFileService>.Instance);
        return (service, store, permissions, user, recordId, file);
    }
}
