using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;

public sealed class FormCatalogFieldsModelTests
{
    [Fact]
    public void Catalog_fields_and_presentation_are_optional_json_and_belong_only_to_manual_catalogs()
    {
        var entity = Entity<BusinessFormCatalog>();
        AssertJson(entity, "FieldsJson", nullable: true);
        AssertJson(entity, "PresentationJson", nullable: true);
        var constraint = Assert.Single(entity.GetCheckConstraints(), value => value.Name == "CK_BusinessFormCatalogs_ManualFields");
        Assert.Equal("\"Kind\" = 'Manual' OR (\"FieldsJson\" IS NULL AND \"PresentationJson\" IS NULL)", constraint.Sql);
    }

    [Fact]
    public void Item_fields_are_required_json_with_an_empty_legacy_default_and_a_containment_index()
    {
        var entity = Entity<BusinessFormCatalogItem>();
        var property = AssertJson(entity, "FieldValuesJson", nullable: false);
        Assert.Equal("{}", property.GetDefaultValue());
        var index = Assert.Single(entity.GetIndexes(), value => value.Properties.Select(member => member.Name).SequenceEqual(["FieldValuesJson"]));
        Assert.Equal("IX_BusinessFormCatalogItems_FieldValuesJson", index.GetDatabaseName());
        Assert.Equal("gin", index.FindAnnotation("Npgsql:IndexMethod")?.Value);
        Assert.Equal(["jsonb_path_ops"], Assert.IsType<string[]>(index.FindAnnotation("Npgsql:IndexOperators")?.Value));
    }

    [Fact]
    public void Captured_fields_are_optional_json_without_a_default_or_a_backfill()
    {
        var property = AssertJson(Entity<BusinessFormRecordReference>(), "FieldValuesJson", nullable: true);
        Assert.Null(property.GetDefaultValue());
    }

    private static IProperty AssertJson(IEntityType entity, string name, bool nullable)
    {
        var property = entity.FindProperty(name);
        Assert.NotNull(property);
        Assert.Equal(typeof(string), property.ClrType);
        Assert.Equal("jsonb", property.GetColumnType());
        Assert.Equal(nullable, property.IsNullable);
        return property;
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
