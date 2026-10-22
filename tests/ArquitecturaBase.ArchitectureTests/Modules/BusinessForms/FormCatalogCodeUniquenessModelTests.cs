using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;

public sealed class FormCatalogCodeUniquenessModelTests
{
    [Fact]
    public void Catalog_code_has_no_EF_index_or_alternate_key()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        options.UseNpgsql();
        options.UseOpenIddict<Guid>();
        using var context = new ApplicationDbContext(options.Options);
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(BusinessFormCatalogItem));
        Assert.NotNull(entity);

        // EF cannot express deferrability. The migration owns the database constraint.
        string[] codeColumns = [nameof(BusinessFormCatalogItem.CatalogId), nameof(BusinessFormCatalogItem.Code)];
        Assert.DoesNotContain(entity.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual(codeColumns));
        Assert.DoesNotContain(entity.GetKeys(), key => key.Properties.Select(property => property.Name).SequenceEqual(codeColumns));

        // Positive control: the real item entity and its offer index remain configured.
        Assert.Equal([nameof(BusinessFormCatalogItem.Id)], entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        string[] offerColumns = [nameof(BusinessFormCatalogItem.CatalogId), nameof(BusinessFormCatalogItem.IsActive),
            nameof(BusinessFormCatalogItem.Position), nameof(BusinessFormCatalogItem.Id)];
        Assert.Contains(entity.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual(offerColumns));
    }
}
