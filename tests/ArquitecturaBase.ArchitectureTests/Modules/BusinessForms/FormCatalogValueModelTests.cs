using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;

public sealed class FormCatalogValueModelTests
{
    [Fact]
    public void Catalog_value_type_is_closed_text_with_a_legacy_default_and_filters_restriction()
    {
        var entity = Entity<BusinessFormCatalog>();
        var property = entity.FindProperty("ValueType");
        Assert.NotNull(property);
        Assert.False(property.IsNullable);
        Assert.True(property.ClrType.IsEnum);
        Assert.Equal(typeof(string), property.GetProviderClrType());
        Assert.Equal(16, property.GetMaxLength());
        Assert.Equal("Text", property.GetDefaultValue()!.ToString());
        var constraint = Assert.Single(entity.GetCheckConstraints(), value => value.Name == "CK_BusinessFormCatalogs_ValueType");
        Assert.Equal("\"ValueType\" IN ('Text','Integer','Decimal','Date') AND (\"Kind\" = 'Manual' OR \"ValueType\" = 'Text')", constraint.Sql);
    }

    [Fact]
    public void Item_value_is_nullable_bounded_text_without_a_default()
    {
        var property = Entity<BusinessFormCatalogItem>().FindProperty("Value");
        Assert.NotNull(property);
        Assert.Equal(typeof(string), property.ClrType);
        Assert.True(property.IsNullable);
        Assert.Equal(64, property.GetMaxLength());
        Assert.Null(property.GetDefaultValue());
    }

    [Fact]
    public void Captured_value_and_type_are_nullable_together_without_rewriting_legacy_references()
    {
        var entity = Entity<BusinessFormRecordReference>();
        var value = entity.FindProperty("Value");
        var type = entity.FindProperty("ValueType");
        Assert.NotNull(value);
        Assert.NotNull(type);
        Assert.True(value.IsNullable);
        Assert.True(type.IsNullable);
        Assert.Equal(typeof(string), value.ClrType);
        Assert.Equal(typeof(string), type.ClrType);
        Assert.Equal(64, value.GetMaxLength());
        Assert.Equal(16, type.GetMaxLength());
        Assert.Null(value.GetDefaultValue());
        Assert.Null(type.GetDefaultValue());
        var constraint = Assert.Single(entity.GetCheckConstraints(), check => check.Name == "CK_BusinessFormRecordReferences_Value");
        Assert.Equal("(\"Value\" IS NULL) = (\"ValueType\" IS NULL)", constraint.Sql);
    }

    private static IEntityType Entity<T>()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        options.UseNpgsql();
        options.UseOpenIddict<Guid>();
        using var context = new ApplicationDbContext(options.Options);
        return context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(T))!;
    }
}
