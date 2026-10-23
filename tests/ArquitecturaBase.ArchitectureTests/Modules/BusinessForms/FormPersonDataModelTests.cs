using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;

public sealed class FormPersonDataModelTests
{
    [Fact]
    public void Person_attributes_keep_their_key_and_validate_the_catalog_source_with_a_restrictive_relationship()
    {
        var entity = Entity<BusinessFormPersonAttribute>();
        Assert.Equal("BusinessFormPersonAttributes", entity.GetTableName());
        Assert.Equal(40, entity.FindProperty("Key")!.GetMaxLength());
        Assert.Equal("jsonb", entity.FindProperty("NameJson")!.GetColumnType());
        Assert.Equal(16, entity.FindProperty("Type")!.GetMaxLength());
        Assert.True(entity.FindProperty("Revision")!.IsConcurrencyToken);
        var key = Assert.Single(entity.GetIndexes(), value => value.Properties.Select(property => property.Name).SequenceEqual(["Key"]));
        Assert.Equal(["Key"], key.Properties.Select(value => value.Name));
        Assert.True(key.IsUnique);
        Assert.Null(key.GetFilter());
        var source = Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(BusinessFormCatalog), source.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, source.DeleteBehavior);
        Assert.Equal(["SourceCatalogId"], source.Properties.Select(value => value.Name));
        Assert.Equal("(\"Type\" = 'Catalog') = (\"SourceCatalogId\" IS NOT NULL)",
            Assert.Single(entity.GetCheckConstraints(), value => value.Name == "CK_BusinessFormPersonAttributes_Source").Sql);
    }

    [Fact]
    public void Person_values_have_an_independent_revision_and_do_not_reference_the_optional_identity_account()
    {
        var entity = Entity<BusinessFormPersonData>();
        Assert.Equal("BusinessFormPersonData", entity.GetTableName());
        var values = entity.FindProperty("ValuesJson")!;
        Assert.Equal("jsonb", values.GetColumnType());
        Assert.False(values.IsNullable);
        Assert.Equal("{}", values.GetDefaultValue());
        Assert.True(entity.FindProperty("Revision")!.IsConcurrencyToken);
        Assert.Empty(entity.GetForeignKeys());
        var user = Assert.Single(entity.GetIndexes());
        Assert.Equal(["UserId"], user.Properties.Select(value => value.Name));
        Assert.True(user.IsUnique);
        Assert.Null(user.GetFilter());
    }

    private static IEntityType Entity<T>()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        options.UseNpgsql();
        options.UseOpenIddict<Guid>();
        using var db = new ApplicationDbContext(options.Options);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(T));
        Assert.NotNull(entity);
        return entity;
    }
}
