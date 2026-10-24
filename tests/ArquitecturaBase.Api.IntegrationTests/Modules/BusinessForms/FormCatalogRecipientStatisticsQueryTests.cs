using System.Reflection;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormCatalogRecipientStatisticsQueryTests
{
    [Fact]
    public void Maximum_offers_filter_before_grouping_and_parameterize_person_keys_without_loading_items()
    {
        using var db = Database(); var reader = new FormCatalogReader(db);
        var method = typeof(FormCatalogReader).GetMethod("FindMaximumOfferSize", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var fieldIds = new[] { Guid.NewGuid(), Guid.NewGuid() }; var filter = "{\"private\":\"value' OR TRUE--\"}";
        var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(reader, [Guid.NewGuid(), filter, fieldIds]));
        using var command = query.CreateDbCommand(); var sql = command.CommandText;
        Assert.Contains("item.\"IsActive\"", sql, StringComparison.Ordinal);
        Assert.Contains("item.\"FieldValuesJson\" @>", sql, StringComparison.Ordinal);
        Assert.Contains("jsonb_object_agg", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY \"Values\"", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)::integer", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT 1", sql, StringComparison.Ordinal);
        Assert.Contains("IS NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(filter, sql, StringComparison.Ordinal);
        Assert.Equal(filter, command.Parameters[1].Value);
        Assert.Equal(fieldIds.Select(id => id.ToString("D")), Assert.IsType<string[]>(command.Parameters[2].Value));
    }

    [Fact]
    public void Missing_recipients_count_once_and_cap_names_after_active_accounts_and_role_permissions()
    {
        using var db = Database(); var reader = new FormPersonDataReader(db);
        var method = typeof(FormPersonDataReader).GetMethod("FindMissingRecipients", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method); var attributes = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(reader, [attributes]));
        using var command = query.CreateDbCommand(); var sql = command.CommandText;
        Assert.Contains("person.\"IsActive\" AND NOT person.\"IsDeleted\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"AspNetRoleClaims\"", sql, StringComparison.Ordinal);
        Assert.Contains("EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN \"BusinessFormPersonData\"", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(*)::integer", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT 5", sql, StringComparison.Ordinal);
        // A catalog value that is not an option of the attribute's current source is missing, as in the people table and the send.
        Assert.Contains("LEFT JOIN \"BusinessFormPersonAttributes\" AS attribute", sql, StringComparison.Ordinal);
        Assert.Contains("item.\"CatalogId\" = attribute.\"SourceCatalogId\"", sql, StringComparison.Ordinal);
        Assert.Contains("pg_input_is_valid", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Email", sql, StringComparison.Ordinal);
        Assert.Equal(attributes.Select(id => id.ToString("D")), Assert.IsType<string[]>(command.Parameters[0].Value));
        Assert.Equal(Permissions.ClaimType, command.Parameters[1].Value);
        Assert.Equal(BusinessFormsPermissions.RecordsCreate, command.Parameters[2].Value);
    }

    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
}
