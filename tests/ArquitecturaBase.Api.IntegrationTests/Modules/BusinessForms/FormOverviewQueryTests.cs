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
        // Who corrected the response or passed it from a document does not decide it (F4): only that is read of the responses.
        Assert.Contains("\"CorrectedByUserId\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"ReviewedByUserId\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ValuesJson\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"LastRecordNumber\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_readers_do_not_query_private_approval_dimensions()
    {
        using var db = Context();
        var sql = Query(db, new(), true, false).ToQueryString();
        // «Respuestas» counts the current responses that are not voided (Etapa 3), no longer LastRecordNumber.
        Assert.DoesNotContain("\"LastRecordNumber\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"BusinessFormRecords\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"ReplacedByRecordId\" IS NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"BusinessFormTasks\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ValuesJson\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SchemaJson\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"BusinessFormVersions\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Who_only_completes_forms_counts_only_their_own_responses_without_approval_dimensions()
    {
        using var db = Context();
        var sql = Query(db, new(), false, false, create: true).ToQueryString();
        Assert.Contains("\"BusinessFormVersions\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"BusinessFormRecords\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"ActorUserId\" =", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"BusinessFormTasks\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"LastRecordNumber\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ValuesJson\"", sql, StringComparison.Ordinal);
    }

    // OT1-C: a column sorts in SQL with the empty values last, the state in the order of its circuit and the number by its
    // prefix; without a sort, the usual order (what awaits approval first) stays.
    [Fact]
    public void A_sorted_column_orders_in_sql_with_empty_values_last_and_the_state_in_its_circuit_order()
    {
        using var db = Context();
        string[] OrderBy(string? sort) => OrderTerms(Query(db, new() { Sort = sort }, true, true).ToQueryString());

        var byState = OrderBy("lastResponseState");
        var byReceived = OrderBy("-lastSubmittedAtUtc");
        var byNumber = OrderBy("lastResponseNumber");
        var usual = OrderBy(null);

        // Ascending, Postgres already leaves an empty state last (the CASE gives it the last value): no repeated subquery.
        Assert.Contains("'Received' THEN 0", byState[0], StringComparison.Ordinal);
        Assert.Contains("'Voided' THEN 4", byState[0], StringComparison.Ordinal);
        Assert.EndsWith("IS NULL", byReceived[0], StringComparison.Ordinal);
        Assert.EndsWith("DESC", byReceived[1], StringComparison.Ordinal);
        Assert.Contains(byNumber, term => term.EndsWith("\"Prefix\"", StringComparison.Ordinal));
        Assert.Contains("> 0", usual[0], StringComparison.Ordinal);
        Assert.EndsWith("DESC", usual[0], StringComparison.Ordinal);
    }

    // The terms of the outer ORDER BY (the one that starts a line), split at its own commas, not at those of its subqueries.
    private static string[] OrderTerms(string sql)
    {
        const string start = "\nORDER BY ";
        var clause = sql[(sql.LastIndexOf(start, StringComparison.Ordinal) + start.Length)..];
        var terms = new List<string>();
        var depth = 0;
        var from = 0;
        for (var index = 0; index < clause.Length; index++)
        {
            depth += clause[index] switch { '(' => 1, ')' => -1, _ => 0 };
            if (clause[index] == ',' && depth == 0)
            {
                terms.Add(clause[from..index].Trim());
                from = index + 1;
            }
        }
        terms.Add(clause[from..].Trim());
        return [.. terms];
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

    private static IQueryable Query(ApplicationDbContext db, ListFormOverviewRequest request, bool read, bool decide, bool create = false)
    {
        var reader = new FormOverviewReader(db);
        var query = typeof(FormOverviewReader).GetMethod("Query", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(query);
        return Assert.IsAssignableFrom<IQueryable>(query.Invoke(reader, [Guid.NewGuid(), request, "en", read, decide, create]));
    }
    private static ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
}