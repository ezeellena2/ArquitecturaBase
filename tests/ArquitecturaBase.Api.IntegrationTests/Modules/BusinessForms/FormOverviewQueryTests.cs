using System.Reflection;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormOverviewQueryTests
{
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
        Assert.Contains("jsonb_array_elements", sql, StringComparison.Ordinal);
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
        var type = typeof(ApplicationDbContext).Assembly.GetType(
            "ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers.FormOverviewReader");
        Assert.NotNull(type);
        var records = new FormRecordReader(db, new DataProtectionFormPublicationProtector(new EphemeralDataProtectionProvider()));
        var reader = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [db, records], null);
        Assert.NotNull(reader);
        var query = type.GetMethod("Query", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(query);
        FormResponseVersionRow[] versions = [new(Guid.NewGuid(), 1, "{\"es\":\"Original\",\"en\":\"Partial\"}", "{\"format\":1,\"fields\":[]}", "es", ["es"])];
        return Assert.IsAssignableFrom<IQueryable>(query.Invoke(reader, [Guid.NewGuid(), request, "en", read, decide, versions]));
    }
    private static ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
}