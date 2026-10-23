using System.Reflection;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormCatalogResponseFieldQueryTests
{
    [Fact]
    public void Historical_sheet_columns_read_only_distinct_latest_metadata_in_one_parameterized_query()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var method = typeof(FormResponseReader).GetMethod("FindHistoricalCatalogFields", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method); var definition = Guid.NewGuid();
        var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(new FormResponseReader(db, null!), [definition]));
        using var command = query.CreateDbCommand(); var sql = command.CommandText;
        Assert.Contains("DISTINCT ON", sql, StringComparison.Ordinal);
        Assert.Contains("jsonb_array_elements", sql, StringComparison.Ordinal);
        Assert.Contains("WITH ORDINALITY", sql, StringComparison.Ordinal);
        Assert.Contains("record.\"Number\" DESC", sql, StringComparison.Ordinal);
        Assert.Contains("record.\"DefinitionId\" = reference.\"DefinitionId\"", sql, StringComparison.Ordinal);
        Assert.Contains("'name'", sql, StringComparison.Ordinal);
        Assert.Contains("'role'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("-> 'value'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("->> 'value'", sql, StringComparison.Ordinal);
        Assert.Equal(definition, command.Parameters[0].Value);
    }
}
