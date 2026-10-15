using ArquitecturaBase.Domain.Files;

namespace ArquitecturaBase.Domain.UnitTests.Files;

public sealed class StoredFileTests
{
    [Fact]
    public void Metadata_keeps_the_owner_original_name_and_content_hash_without_exposing_them_in_diagnostics()
    {
        var owner = Guid.CreateVersion7();
        var file = StoredFile.Create(owner, "foto de la obra.png", "image/png", 123, new string('a', 64), "receipt/file/original", "receipt/file/thumbnail");
        Assert.Equal(owner, file.OwnerUserId);
        Assert.Equal("foto de la obra.png", file.Name);
        Assert.Equal(new string('a', 64), file.Sha256);
        Assert.Equal(123, file.Size);
        Assert.Equal(7, file.Id.Version);
        Assert.DoesNotContain(file.Name, file.ToString());
        Assert.DoesNotContain(file.StorageKey, file.ToString());
    }
}
