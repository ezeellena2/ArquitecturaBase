using System.Reflection;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormDefinitionQueryTests
{
    [Theory]
    [InlineData("Active", "IS NULL")]
    [InlineData("Published", "IS NULL")]
    [InlineData("Drafts", "IS NULL")]
    [InlineData("Deactivated", "IS NOT NULL")]
    public void Lifecycle_filters_and_authoring_audit_remain_composable_in_postgresql(string filter, string expectedPredicate)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader = new FormDefinitionReader(db, new DataProtectionFormPublicationProtector(new EphemeralDataProtectionProvider()));
        var method = typeof(FormDefinitionReader).GetMethod("ListQuery", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(reader,
            [new ListFormDefinitionsRequest { PublicationFilter = filter, Sort = "-lastChangedAtUtc", DisplayCulture = "en" }]));
        var sql = query.ToQueryString();

        var where = Regex.Match(sql, "(?ms)^WHERE (?<predicate>.*?)(?=^ORDER BY |\\z)");
        Assert.True(where.Success, "The outer query must filter definitions.");
        var predicate = where.Groups["predicate"].Value.Trim();
        Assert.Matches("(?:^|\\bAND )[a-z0-9]+[.]\"DeactivatedAtUtc\" " + expectedPredicate + "(?:$| AND\\b)", predicate);
        var oppositePredicate = expectedPredicate == "IS NULL" ? "IS NOT NULL" : "IS NULL";
        Assert.DoesNotMatch("[a-z0-9]+[.]\"DeactivatedAtUtc\" " + oppositePredicate, predicate);
        Assert.Matches("\"ConfirmedRevision\" <= [a-z0-9]+[.]\"Revision\"", sql);
        Assert.Matches("[)]\\s*< [a-z0-9]+[.]\"Revision\"", sql);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ModifiedAtUtc\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ProtectedResult\"", sql, StringComparison.Ordinal);
    }
}