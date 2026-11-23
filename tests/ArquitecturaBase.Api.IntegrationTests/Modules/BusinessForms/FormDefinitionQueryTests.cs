using System.Reflection;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
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
        var reader = new FormDefinitionReader(db);
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

    // FU2-A: «Pending» mixes active and deactivated forms and, like its order and its count, reads in the same statement the tasks
    // that await the decision of whoever asks, with the predicate of «Formularios»: current roles, an active account, not the
    // author, nor who corrected the response or passed it from a document.
    [Fact]
    public void The_pending_filter_and_its_order_read_the_decidable_tasks_in_the_same_statement()
    {
        var sql = ListSql(new ListFormDefinitionsRequest { PublicationFilter = "Pending", Sort = "-pendingDecisionCount", DisplayCulture = "en", DeciderUserId = Guid.NewGuid() });

        var where = Regex.Match(sql, "(?ms)^WHERE (?<predicate>.*?)(?=^ORDER BY |\\z)");
        Assert.True(where.Success, "The outer query must filter definitions.");
        var predicate = where.Groups["predicate"].Value;
        Assert.DoesNotMatch("[a-z0-9]+[.]\"DeactivatedAtUtc\" IS (?:NOT )?NULL", predicate);
        Assert.Contains("\"BusinessFormTasks\"", predicate, StringComparison.Ordinal);
        Assert.Contains("\"BusinessFormTasks\"", sql[sql.LastIndexOf("\nORDER BY ", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("\"AspNetUserRoles\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"IsActive\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"AuthorUserId\" <>", sql, StringComparison.Ordinal);
        Assert.Contains("\"CorrectedByUserId\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"ReviewedByUserId\"", sql, StringComparison.Ordinal);
        Assert.Contains("'Open'", sql, StringComparison.Ordinal);
    }

    // Without someone who decides (the policy sets it only with the permission), the list does not read the tasks at all.
    [Fact]
    public void Without_a_decider_the_list_does_not_read_the_tasks()
    {
        var sql = ListSql(new ListFormDefinitionsRequest { DisplayCulture = "en" });
        Assert.DoesNotContain("\"BusinessFormTasks\"", sql, StringComparison.Ordinal);
    }

    private static string ListSql(ListFormDefinitionsRequest request)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var method = typeof(FormDefinitionReader).GetMethod("ListQuery", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IQueryable>(method.Invoke(new FormDefinitionReader(db), [request])).ToQueryString();
    }
}