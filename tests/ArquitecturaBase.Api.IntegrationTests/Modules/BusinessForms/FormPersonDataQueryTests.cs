using System.Reflection;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormPersonDataQueryTests
{
    [Fact]
    public void People_search_and_page_only_display_names_and_keep_inactive_accounts_without_returning_email()
    {
        using var db = Database();
        var query = Query(db, "FindPeople", new ListFormPersonDataRequest { Search = "person", Sort = "-displayName", Page = 2, PageSize = 7 });
        var sql = query.ToQueryString();
        Assert.Contains("ILIKE", sql, StringComparison.Ordinal);
        Assert.Contains("DisplayName", sql, StringComparison.Ordinal);
        Assert.Contains("ValuesJson", sql, StringComparison.Ordinal);
        Assert.Contains("IsDeleted", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("IsActive", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Email", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Attribute_usages_cover_drafts_and_active_versions_with_one_batch_and_keep_deactivated_forms()
    {
        using var db = Database();
        var sql = Query(db, "FindAttributeUsages", new[] { Guid.NewGuid(), Guid.NewGuid() }).ToQueryString();
        Assert.Contains("UNNEST", sql, StringComparison.Ordinal);
        Assert.Contains("requested.\"AttributeId\"", sql, StringComparison.Ordinal);
        Assert.Contains("personAttributeId", sql, StringComparison.Ordinal);
        Assert.Contains("definition.\"SchemaJson\" -> 'fields'", sql, StringComparison.Ordinal);
        Assert.Contains("version.\"SchemaJson\" -> 'questions'", sql, StringComparison.Ordinal);
        Assert.Contains("version.\"Id\" = definition.\"ActiveVersionId\"", sql, StringComparison.Ordinal);
        Assert.Contains("version.\"DefinitionId\" = definition.\"Id\"", sql, StringComparison.Ordinal);
        Assert.Contains("UNION", sql, StringComparison.Ordinal);
        Assert.Contains("NOT definition.\"IsDeleted\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DeactivatedAtUtc", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Person_options_are_active_manual_items_with_localized_search_and_a_stable_order()
    {
        using var db = Database();
        var sql = Query(db, "FindOptions", Guid.NewGuid(), new ListFormPersonAttributeOptionsRequest { DisplayCulture = "en", Search = "warehouse" }).ToQueryString();
        Assert.Contains("Manual", sql, StringComparison.Ordinal);
        Assert.Contains("IsActive", sql, StringComparison.Ordinal);
        Assert.Contains("TextEs", sql, StringComparison.Ordinal);
        Assert.Contains("TextEn", sql, StringComparison.Ordinal);
        Assert.Contains("ILIKE", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
    }

    private static IQueryable Query(ApplicationDbContext db, string name, params object[] arguments)
    {
        var type = typeof(FormPersonDataReader);
        var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IQueryable>(method.Invoke(new FormPersonDataReader(db), arguments));
    }

    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
}
