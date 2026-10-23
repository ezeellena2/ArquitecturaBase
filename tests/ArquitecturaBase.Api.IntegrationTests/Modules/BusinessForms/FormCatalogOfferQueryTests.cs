using System.Reflection;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormCatalogOfferQueryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("{\"zone\":4}")]
    public void Offers_apply_containment_before_the_ordered_201_row_limit(string? containment)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var method = typeof(FormCatalogReader).GetMethod("FindOfferItems", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(new FormCatalogReader(db), [Guid.NewGuid(), containment]));

        var sql = query.ToQueryString();

        Assert.Contains("\"CatalogId\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"IsActive\"", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
        Assert.Contains("201", sql, StringComparison.Ordinal);
        if (containment is null)
            Assert.DoesNotContain("@>", sql, StringComparison.Ordinal);
        else
            Assert.True(sql.IndexOf("@>", StringComparison.Ordinal) < sql.IndexOf("LIMIT", StringComparison.Ordinal));
    }
}
