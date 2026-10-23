using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogNoChangeImportTests
{
    [Fact]
    public async Task Applying_a_sheet_with_no_differences_keeps_revision_and_the_previous_import_marker()
    {
        using var fixture = new FormCatalogImportFixture();
        var first = await fixture.Service.ApplyAsync(fixture.Catalog.Id, fixture.Request(), TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess);
        var revision = fixture.Catalog.Revision;
        var importedAt = fixture.Catalog.LastImportAtUtc;
        fixture.Clock.Advance(TimeSpan.FromHours(1));

        var preview = await fixture.Service.PreviewAsync(fixture.Catalog.Id, fixture.Request(), TestContext.Current.CancellationToken);
        Assert.True(preview.IsSuccess);
        Assert.Equal(0, preview.Value.Counts.Changed);
        Assert.Equal(0, preview.Value.Counts.Added);
        var applied = await fixture.Service.ApplyAsync(fixture.Catalog.Id, fixture.Request(), TestContext.Current.CancellationToken);

        Assert.True(applied.IsSuccess);
        Assert.Equal(revision, fixture.Catalog.Revision);
        Assert.Equal(importedAt, fixture.Catalog.LastImportAtUtc);
        Assert.Equal(revision, applied.Value.Catalog.Revision);
    }
}
