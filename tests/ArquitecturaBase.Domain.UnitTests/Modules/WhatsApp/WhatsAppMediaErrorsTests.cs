using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

// N22: a download from WhatsApp and the private storage say apart why a file is missing; both may pass, and what the file is never does.
public sealed class WhatsAppMediaErrorsTests
{
    [Fact]
    public void A_download_from_WhatsApp_and_the_private_storage_may_pass()
    {
        Assert.True(WhatsAppMediaErrors.MayPass(WhatsAppMediaErrors.Unavailable));
        Assert.True(WhatsAppMediaErrors.MayPass(FileErrors.StorageUnavailable));
    }

    [Fact]
    public void What_the_file_is_or_a_missing_copy_does_not_pass()
    {
        Error[] final = [FileErrors.Missing, FileErrors.NotFound, FileErrors.TooLarge, FileErrors.HashMismatch, FileErrors.InvalidType, FileErrors.InvalidMetadata];
        Assert.All(final, error => Assert.False(WhatsAppMediaErrors.MayPass(error)));
    }

    [Fact]
    public void The_download_from_WhatsApp_has_its_own_code()
    {
        Assert.Equal("WhatsApp.Media.Unavailable", WhatsAppMediaErrors.Unavailable.Code);
        Assert.Equal("Files.Storage.Unavailable", FileErrors.StorageUnavailable.Code);
        Assert.Equal("Files.Content.Missing", FileErrors.Missing.Code);
    }
}
