using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogImportMarkerTests
{
    private static readonly DateTime ImportedAtUtc = new(2026, 10, 22, 1, 2, 3, DateTimeKind.Utc);
    private static string Hash => new('a', 64);
    private static BusinessFormCatalog Catalog() => BusinessFormCatalog.CreateManual("es", 
        new Dictionary<string, string> { ["es"] = "Opciones", ["en"] = "Options" },
        new Dictionary<string, string>());

    [Fact]
    public void Applying_an_import_marks_its_hash_and_time_and_advances_one_revision()
    {
        var catalog = Catalog();
        Assert.True(catalog.MarkImported(1, Hash, ImportedAtUtc).IsSuccess);
        Assert.Equal(Hash, catalog.LastImportSha256);
        Assert.Equal(ImportedAtUtc, catalog.LastImportAtUtc);
        Assert.Equal(2, catalog.Revision);
        Assert.True(catalog.MarkImported(2, new('b', 64), ImportedAtUtc.AddMinutes(1)).IsSuccess);
        Assert.Equal(new('b', 64), catalog.LastImportSha256);
        Assert.Equal(ImportedAtUtc.AddMinutes(1), catalog.LastImportAtUtc);
        Assert.Equal(3, catalog.Revision);
    }

    [Fact]
    public void A_stale_import_does_not_overwrite_the_last_applied_marker()
    {
        var catalog = Catalog();
        Assert.True(catalog.MarkImported(1, Hash, ImportedAtUtc).IsSuccess);
        var result = catalog.MarkImported(1, new('b', 64), ImportedAtUtc.AddMinutes(1));
        Assert.Equal(FormCatalogErrors.RevisionConflict, result.Error);
        Assert.Equal(Hash, catalog.LastImportSha256);
        Assert.Equal(ImportedAtUtc, catalog.LastImportAtUtc);
        Assert.Equal(2, catalog.Revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void A_marker_requires_a_canonical_sha256(string? hash)
    {
        var catalog = Catalog();
        Assert.Equal(FormCatalogErrors.ContentInvalid, catalog.MarkImported(1, hash!, ImportedAtUtc).Error);
        Assert.Null(catalog.LastImportSha256);
        Assert.Null(catalog.LastImportAtUtc);
        Assert.Equal(1, catalog.Revision);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void The_import_instant_must_be_UTC(DateTimeKind kind)
    {
        var catalog = Catalog();
        Assert.Equal(FormCatalogErrors.ContentInvalid, catalog.MarkImported(1, Hash, DateTime.SpecifyKind(ImportedAtUtc, kind)).Error);
        Assert.Equal(1, catalog.Revision);
        Assert.Null(catalog.LastImportSha256);
    }

    [Fact]
    public void Filters_cannot_be_marked_as_manual_imports()
    {
        var catalog = BusinessFormCatalog.CreateFilters("es", new Dictionary<string, string> { ["es"] = "Personas" }, new Dictionary<string, string>(), "{}");
        Assert.Equal(FormCatalogErrors.ManualItemsUnavailable, catalog.MarkImported(1, Hash, ImportedAtUtc).Error);
        Assert.Equal(1, catalog.Revision);
        Assert.Null(catalog.LastImportAtUtc);
    }
}
