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

    [Fact]
    public void Missing_counts_group_each_requested_attribute_over_the_people_table_filter()
    {
        using var db = Database();
        var attributes = new[] { Guid.NewGuid(), Guid.NewGuid() };
        using var command = Query(db, "CountMissingByAttribute", attributes, " 50%_off ").CreateDbCommand();
        var sql = command.CommandText;
        Assert.Contains("UNNEST", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN \"AspNetUsers\" AS person ON NOT person.\"IsDeleted\"", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN \"BusinessFormPersonData\" AS data", sql, StringComparison.Ordinal);
        Assert.Contains("ILIKE", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(person.\"Id\") FILTER", sql, StringComparison.Ordinal);
        Assert.Contains("= 'null'::jsonb", sql, StringComparison.Ordinal);
        // A catalog value that is not an option of the attribute's current source is missing, as in the table and the send.
        Assert.Contains("LEFT JOIN \"BusinessFormPersonAttributes\" AS attribute", sql, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("item.\"CatalogId\" = attribute.\"SourceCatalogId\"", sql, StringComparison.Ordinal);
        Assert.Contains("pg_input_is_valid", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY requested.key", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("IsActive", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Email", sql, StringComparison.Ordinal);
        Assert.Equal(attributes.Select(id => id.ToString("D")), Assert.IsType<string[]>(command.Parameters[0].Value));
        Assert.Equal("%50\\%\\_off%", command.Parameters[1].Value);
        using var all = Query(db, "CountMissingByAttribute", attributes, null!).CreateDbCommand();
        Assert.Equal("", all.Parameters[1].Value);
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
