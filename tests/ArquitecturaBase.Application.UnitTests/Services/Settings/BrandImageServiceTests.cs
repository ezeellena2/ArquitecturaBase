using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Caching;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Application.Services.Settings;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Application.Validation.Settings;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Services.Settings;

// ME2b: the logo and the icon are saved when uploaded or removed, with the revision the screen read and one commit.
public sealed class BrandImageServiceTests
{
    private static readonly byte[] Bytes = [1, 2, 3, 4];

    [Fact]
    public async Task An_uploaded_logo_is_stored_with_a_new_key_and_saved_with_one_revision()
    {
        var fixture = new Fixture { Image = new(BrandImageFormat.Png, 400, 112) };

        var result = await fixture.Service.UploadAsync(new(BrandImageKind.Logo, Bytes, 1), Ct);

        Assert.True(result.IsSuccess);
        var key = Assert.Single(fixture.Files.Modified.Keys);
        Assert.StartsWith("branding/logo/", key, StringComparison.Ordinal);
        Assert.EndsWith(".png", key, StringComparison.Ordinal);
        Assert.Equal(key, fixture.Settings.LogoFileKey);
        Assert.Equal(new BrandImageResponse(2, BrandImage.VersionOf(key)), result.Value);
        Assert.Equal(["commit", "invalidate"], fixture.Events);
    }

    [Fact]
    public async Task A_jpeg_logo_keeps_its_format()
    {
        var fixture = new Fixture { Image = new(BrandImageFormat.Jpeg, 800, 200) };

        var result = await fixture.Service.UploadAsync(new(BrandImageKind.Logo, Bytes, 1), Ct);

        Assert.True(result.IsSuccess);
        Assert.EndsWith(".jpg", fixture.Settings.LogoFileKey, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BrandImageKind.Logo, BrandImageFormat.Png, 112, 400, SettingsErrors.LogoInvalidCode)]
    [InlineData(BrandImageKind.Icon, BrandImageFormat.Jpeg, 192, 192, SettingsErrors.IconInvalidCode)]
    [InlineData(BrandImageKind.Icon, BrandImageFormat.Png, 192, 100, SettingsErrors.IconInvalidCode)]
    public async Task An_image_with_the_wrong_shape_or_format_is_refused_on_the_file_field(BrandImageKind kind, BrandImageFormat format,
        int width, int height, string code)
    {
        var fixture = new Fixture { Image = new(format, width, height) };

        var result = await fixture.Service.UploadAsync(new(kind, Bytes, 1), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(code, error.Code);
        Assert.True(error.Errors.ContainsKey("file"));
        Assert.Empty(fixture.Files.Modified);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task Bytes_that_are_not_an_image_are_refused()
    {
        var fixture = new Fixture { Image = null };

        var result = await fixture.Service.UploadAsync(new(BrandImageKind.Logo, Bytes, 1), Ct);

        Assert.Equal(SettingsErrors.LogoInvalidCode, result.Error.Code);
        Assert.Empty(fixture.Files.Modified);
    }

    [Theory]
    [InlineData("es", "El ícono tiene que ser PNG cuadrado, de 64 a 1024 píxeles y hasta 1 MB.")]
    [InlineData("en", "The icon must be a square PNG from 64 to 1024 pixels, up to 1 MB.")]
    public async Task A_file_over_the_limit_says_the_rule_of_its_image_before_reading_it(string culture, string message)
    {
        using var cultureScope = new CultureScope(culture);
        var fixture = new Fixture { Image = new(BrandImageFormat.Png, 192, 192) };

        var result = await fixture.Service.UploadAsync(new(BrandImageKind.Icon, new byte[BrandImage.MaxBytes + 1], 1), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal([message], error.Errors["file"]);
        Assert.Equal(0, fixture.Inspections);
    }

    [Fact]
    public async Task A_stale_revision_is_a_conflict_and_the_logo_does_not_change()
    {
        var fixture = new Fixture { Image = new(BrandImageFormat.Png, 400, 112) };

        var result = await fixture.Service.UploadAsync(new(BrandImageKind.Logo, Bytes, 7), Ct);

        Assert.Equal(SettingsErrors.RevisionConflict, result.Error);
        Assert.Null(fixture.Settings.LogoFileKey);
        Assert.Equal(1, fixture.Settings.Revision);
        Assert.DoesNotContain("invalidate", fixture.Events);
    }

    [Fact]
    public async Task Without_the_file_store_the_upload_says_so_on_the_file_field()
    {
        var fixture = new Fixture(available: false) { Image = new(BrandImageFormat.Png, 400, 112) };

        var result = await fixture.Service.UploadAsync(new(BrandImageKind.Logo, Bytes, 1), Ct);

        Assert.Equal(FileErrors.StorageUnavailable.Code, result.Error.Code);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task Removing_the_icon_clears_it_with_one_revision()
    {
        var fixture = new Fixture();
        fixture.Settings.SetBrandImage(BrandImageKind.Icon, "branding/icon/0193a4c27d1e7c3a9f001a2b3c4d5e6f.png");

        var result = await fixture.Service.RemoveAsync(BrandImageKind.Icon, 2, Ct);

        Assert.Equal(new BrandImageResponse(3, null), result.Value);
        Assert.Null(fixture.Settings.IconFileKey);
        Assert.Equal(["commit", "invalidate"], fixture.Events);
    }

    [Fact]
    public async Task The_current_logo_opens_with_its_type_and_version()
    {
        var fixture = new Fixture { Image = new(BrandImageFormat.Png, 400, 112) };
        await fixture.Service.UploadAsync(new(BrandImageKind.Logo, Bytes, 1), Ct);

        var opened = await fixture.Service.OpenAsync(BrandImageKind.Logo, Ct);
        var icon = await fixture.Service.OpenAsync(BrandImageKind.Icon, Ct);

        Assert.Equal("image/png", opened.Value.ContentType);
        Assert.Equal(BrandImage.VersionOf(fixture.Settings.LogoFileKey!), opened.Value.Version);
        Assert.Equal(FileErrors.NotFound, icon.Error);
    }

    // ME2c: the cleanup keeps only what the settings point to.
    [Fact]
    public async Task The_retention_source_keeps_only_the_current_logo_and_icon()
    {
        var fixture = new Fixture();
        const string logo = "branding/logo/0193a4c27d1e7c3a9f001a2b3c4d5e6f.png";
        const string old = "branding/logo/0193a4c27d1e7c3a9f00aaaaaaaaaaaa.png";
        fixture.Settings.SetBrandImage(BrandImageKind.Logo, logo);
        var source = new BrandImageRetentionSource(fixture.Repository);

        var kept = await source.ListReferencedKeysAsync([logo, old, "form-files/x.png"], Ct);

        Assert.Equal([logo], kept);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Fixture
    {
        public Fixture(bool available = true)
        {
            Files = new MemoryPrivateFileStore(available);
            UnitOfWork = new FakeUnitOfWork(Events);
            Repository = new FakeRepository(this);
            Service = new BrandImageService(Repository, new FakeCache(Events), UnitOfWork,
                RequestValidators.For(new UploadBrandImageRequestValidator()), Files, new FakeInspector(this),
                new FakeLogger<BrandImageService>());
        }

        public List<string> Events { get; } = [];
        public SystemSettings Settings { get; } = SystemSettings.Create(RegistrationMode.InviteOnly);
        public MemoryPrivateFileStore Files { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public ISystemSettingsRepository Repository { get; }
        public BrandImageService Service { get; }
        public InspectedImage? Image { get; init; }
        public int Inspections { get; set; }

        private sealed class FakeRepository(Fixture fixture) : ISystemSettingsRepository
        {
            public Task<SystemSettings?> GetAsync(CancellationToken cancellationToken) => Task.FromResult<SystemSettings?>(fixture.Settings);
            public Task LockAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public void Add(SystemSettings settings) => throw new NotSupportedException();
        }

        private sealed class FakeCache(List<string> events) : ISystemSettingsCache
        {
            public Task InvalidateAsync(CancellationToken cancellationToken)
            {
                events.Add("invalidate");
                return Task.CompletedTask;
            }
        }

        private sealed class FakeInspector(Fixture fixture) : IImageInspector
        {
            public InspectedImage? Inspect(byte[] content)
            {
                fixture.Inspections++;
                return fixture.Image;
            }
        }
    }
}
