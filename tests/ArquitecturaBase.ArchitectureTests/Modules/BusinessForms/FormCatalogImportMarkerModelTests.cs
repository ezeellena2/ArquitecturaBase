using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;

public sealed class FormCatalogImportMarkerModelTests
{
    [Fact]
    public void Import_marker_has_bounded_nullable_hash_and_timestamp_without_legacy_defaults()
    {
        using var context = Context();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(BusinessFormCatalog))!;
        var hash = entity.FindProperty("LastImportSha256");
        var timestamp = entity.FindProperty("LastImportAtUtc");
        Assert.NotNull(hash); Assert.NotNull(timestamp);
        Assert.Equal(typeof(string), hash.ClrType); Assert.Equal(64, hash.GetMaxLength());
        Assert.Equal("character(64)", hash.GetColumnType());
        Assert.True(hash.IsNullable); Assert.True(timestamp.IsNullable);
        Assert.Equal(typeof(DateTime?), timestamp.ClrType);
        Assert.Null(hash.GetDefaultValue()); Assert.Null(timestamp.GetDefaultValue());
    }

    [Fact]
    public void Import_marker_columns_are_null_together()
    {
        using var context = Context();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(BusinessFormCatalog))!;
        var check = Assert.Single(entity.GetCheckConstraints(), value => value.Name == "CK_BusinessFormCatalogs_LastImport");
        Assert.Equal("(\"LastImportSha256\" IS NULL) = (\"LastImportAtUtc\" IS NULL)", check.Sql);
    }

    private static ApplicationDbContext Context()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        options.UseNpgsql(); options.UseOpenIddict<Guid>();
        return new(options.Options);
    }
}
