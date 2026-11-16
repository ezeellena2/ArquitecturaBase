using System.Reflection;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormCatalogResponseFieldQueryTests
{
    [Fact]
    public void Historical_sheet_columns_read_the_capture_table_in_one_parameterized_query()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var method = typeof(FormResponseReader).GetMethod("FindHistoricalCatalogFields", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method); var definition = Guid.NewGuid(); var catalog = Guid.NewGuid();
        var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(new FormResponseReader(db, null!), [definition, new[] { catalog }]));
        using var command = query.CreateDbCommand(); var sql = command.CommandText;
        Assert.Contains("\"BusinessFormRecordCatalogFields\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"DefinitionId\" = @", sql, StringComparison.Ordinal);
        Assert.Contains("\"CatalogId\" = ANY (@", sql, StringComparison.Ordinal);
        // The capture keeps the fields: reading them never scans the sealed records' JSON.
        Assert.DoesNotContain("jsonb_array_elements", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"BusinessFormRecords\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(definition.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(command.Parameters.Cast<System.Data.Common.DbParameter>(), parameter => Equals(parameter.Value, definition));
    }
}
