using System.Reflection;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormOverviewQueryTests
{
    [Fact]
    public void Overview_resolves_published_names_in_sql_without_loading_schemas_or_protected_manifests()
    {
        using var db = Context();
        var sql = Query(db, new(), true, true).ToQueryString();

        Assert.Contains("\"BusinessFormVersions\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"PublishedCultures\"", sql, StringComparison.Ordinal);
        Assert.Contains("IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"DisplayNameEn\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"DisplayNameEs\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("jsonb_array_elements", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SchemaJson\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ProtectedManifest\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Decidable_pending_uses_current_roles_and_excludes_the_response_author()
    {
        using var db = Context();
        var sql = Query(db, new(), false, true).ToQueryString();
        Assert.Contains("\"AspNetUserRoles\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"IsActive\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"AuthorUserId\" <>", sql, StringComparison.Ordinal);
        Assert.Contains("'Open'", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"BusinessFormRecords\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"LastRecordNumber\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_readers_do_not_query_private_approval_dimensions()
    {
        using var db = Context();
        var sql = Query(db, new(), true, false).ToQueryString();
        Assert.Contains("\"LastRecordNumber\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"BusinessFormRecords\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"BusinessFormTasks\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ValuesJson\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SchemaJson\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"BusinessFormVersions\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_tasks_have_a_definition_and_role_index_for_overview_and_oldest_first_navigation()
    {
        using var db = Context();
        var task = db.Model.FindEntityType(typeof(BusinessFormTask));
        Assert.NotNull(task);
        var index = Assert.Single(task.GetIndexes(), candidate => candidate.Properties.Select(property => property.Name)
            .SequenceEqual(["DefinitionId", "RoleId", "RequestedAtUtc", "Id"]));
        Assert.Equal("\"State\" = 'Open'", index.GetFilter());
        Assert.False(index.IsUnique);
    }

    private static IQueryable Query(ApplicationDbContext db, ListFormOverviewRequest request, bool read, bool decide)
    {
        var reader = new FormOverviewReader(db);
        var query = typeof(FormOverviewReader).GetMethod("Query", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(query);
        return Assert.IsAssignableFrom<IQueryable>(query.Invoke(reader, [Guid.NewGuid(), request, "en", read, decide]));
    }
    private static ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
}