using System.Reflection;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormCatalogUsageQueryTests
{
    [Fact]
    public void Catalog_usage_counts_distinct_non_deleted_definitions_and_keeps_deactivated_ones()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader = new FormCatalogReader(db);
        var method = typeof(FormCatalogReader).GetMethod("FindUsageCounts", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(reader, []));

        var sql = query.ToQueryString();

        Assert.Contains("COUNT(DISTINCT definition.\"Id\")", sql, StringComparison.Ordinal);
        Assert.Contains("NOT definition.\"IsDeleted\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DeactivatedAtUtc", sql, StringComparison.Ordinal);
    }
}
