using System.Reflection;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormCatalogFieldUsageQueryTests
{
    [Fact]
    public void Field_usage_checks_match_and_presentation_in_drafts_and_active_versions_without_excluding_deactivated_forms()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var method = typeof(FormCatalogReader).GetMethod("FieldUsages", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(IReadOnlyCollection<Guid>), method.GetParameters()[1].ParameterType);
        var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(new FormCatalogReader(db), [Guid.NewGuid(), new[] { Guid.NewGuid(), Guid.NewGuid() }, "en"]));

        var sql = query.ToQueryString();

        Assert.Contains("definition.\"SchemaJson\" -> 'fields'", sql, StringComparison.Ordinal);
        Assert.Contains("version.\"SchemaJson\" -> 'questions'", sql, StringComparison.Ordinal);
        Assert.Contains("version.\"Id\" = definition.\"ActiveVersionId\"", sql, StringComparison.Ordinal);
        Assert.Contains("version.\"DefinitionId\" = definition.\"Id\"", sql, StringComparison.Ordinal);
        Assert.Contains("'match'", sql, StringComparison.Ordinal);
        Assert.Contains("'presentation', 'title'", sql, StringComparison.Ordinal);
        Assert.Contains("'presentation', 'description'", sql, StringComparison.Ordinal);
        Assert.Contains("'Draft'", sql, StringComparison.Ordinal);
        Assert.Contains("'Active'", sql, StringComparison.Ordinal);
        Assert.Contains("UNNEST", sql, StringComparison.Ordinal);
        Assert.Contains("requested.\"FieldId\"", sql, StringComparison.Ordinal);
        Assert.Contains("NOT definition.\"IsDeleted\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DeactivatedAtUtc", sql, StringComparison.Ordinal);
    }
}
