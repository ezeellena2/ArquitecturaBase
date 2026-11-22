using System.Reflection;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// OT1-C: «Documentos recibidos» and «Formularios que faltan» sort by every column with a datum, in SQL; these tests build it without a
// connection.
public sealed class FormListSortQueryTests
{
    // The inbox sorts the sender and the form by the name its row shows (the form's in the reader's language or, without it, in the other),
    // each a subquery of the same statement; the state in the order of the circuit with a CASE, a proposed one waiting for its person before
    // one for the team; and the response by its prefix and number. Empty values go last; equal ones keep the usual order, the newest first.
    [Fact]
    public void The_inbox_sorts_in_sql_by_the_names_it_shows_with_its_state_in_the_circuit_order_and_empty_values_last()
    {
        using var db = Context();
        string[] OrderBy(string sort, string culture = "es") => OrderTerms(Inbox(db, sort, culture).ToQueryString());

        var bySender = OrderBy("-sender");
        var byForm = OrderBy("form", "en");
        var byState = OrderBy("state");
        var byDeletion = OrderBy("-deleteAfterUtc");
        var byRecord = OrderBy("record");
        var byDocument = OrderBy("document");

        Assert.EndsWith("IS NULL", bySender[0], StringComparison.Ordinal);
        Assert.Contains("\"AspNetUsers\"", bySender[1], StringComparison.Ordinal);
        Assert.EndsWith("DESC", bySender[1], StringComparison.Ordinal);
        Assert.EndsWith("\"ReceivedAtUtc\" DESC", bySender[2], StringComparison.Ordinal);
        // Ascending, Postgres already leaves a document without a form last: its subquery is not repeated.
        Assert.Contains("\"DisplayNameEn\"", byForm[0], StringComparison.Ordinal);
        Assert.Contains("\"DisplayNameEs\"", byForm[0], StringComparison.Ordinal);
        Assert.EndsWith("\"ReceivedAtUtc\" DESC", byForm[1], StringComparison.Ordinal);
        Assert.Contains("'Proposed' THEN 0", byState[0], StringComparison.Ordinal);
        Assert.Contains("'Discarded' THEN 3", byState[0], StringComparison.Ordinal);
        Assert.Contains("\"ReplyExpiresAtUtc\"", byState[1], StringComparison.Ordinal);
        Assert.EndsWith("\"ReceivedAtUtc\" DESC", byState[2], StringComparison.Ordinal);
        Assert.EndsWith("\"DeleteAfterUtc\" IS NULL", byDeletion[0], StringComparison.Ordinal);
        Assert.EndsWith("\"DeleteAfterUtc\" DESC", byDeletion[1], StringComparison.Ordinal);
        Assert.EndsWith("IS NULL", byRecord[0], StringComparison.Ordinal);
        Assert.Contains("\"Prefix\"", byRecord[1], StringComparison.Ordinal);
        Assert.Contains("\"Number\"", byRecord[2], StringComparison.Ordinal);
        Assert.EndsWith("\"FileName\"", byDocument[0], StringComparison.Ordinal);
        Assert.EndsWith("\"Id\"", byDocument[^1], StringComparison.Ordinal);
    }

    // «Formularios que faltan» sorts by the counts of each topic and by who discarded it, by the name of their account, in the same statement;
    // an empty one goes last, also descending. Equal values keep the usual order, the last request first.
    [Fact]
    public void Missing_topics_sort_in_sql_by_their_counts_and_by_who_discarded_them_with_empty_values_last()
    {
        using var db = Context();
        string[] OrderBy(string sort) => OrderTerms(Missing(db, sort).ToQueryString());

        var byRequests = OrderBy("-requestCount");
        var byPeople = OrderBy("personCount");
        var byDiscarder = OrderBy("-discardedBy");
        var byDiscardedAt = OrderBy("discardedAtUtc");
        var byName = OrderBy("name");

        Assert.EndsWith("\"RequestCount\" DESC", byRequests[0], StringComparison.Ordinal);
        Assert.EndsWith("\"LastRequestedAtUtc\" DESC", byRequests[1], StringComparison.Ordinal);
        Assert.EndsWith("\"PersonCount\"", byPeople[0], StringComparison.Ordinal);
        Assert.EndsWith("IS NULL", byDiscarder[0], StringComparison.Ordinal);
        Assert.Contains("\"AspNetUsers\"", byDiscarder[1], StringComparison.Ordinal);
        Assert.EndsWith("DESC", byDiscarder[1], StringComparison.Ordinal);
        // Ascending, Postgres already leaves an empty date last.
        Assert.EndsWith("\"DiscardedAtUtc\"", byDiscardedAt[0], StringComparison.Ordinal);
        Assert.EndsWith("\"LastRequestedAtUtc\" DESC", byDiscardedAt[1], StringComparison.Ordinal);
        Assert.EndsWith("\"Name\"", byName[0], StringComparison.Ordinal);
        Assert.EndsWith("\"Id\"", byName[^1], StringComparison.Ordinal);
    }

    // The inbox's sorted rows, without its filters (they come before) and without running them.
    private static IQueryable Inbox(ApplicationDbContext db, string sort, string culture)
    {
        var sorted = typeof(FormReceivedDocumentReader).GetMethod("Sorted", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(sorted);
        return Assert.IsAssignableFrom<IQueryable>(sorted.Invoke(new FormReceivedDocumentReader(db), [db.Set<FormReceivedDocument>(),
            new ListFormReceivedDocumentsRequest { Sort = sort }, culture, new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc)]));
    }

    // The topics with their counts, sorted, without running them.
    private static IQueryable Missing(ApplicationDbContext db, string sort)
    {
        var aggregates = typeof(FormMissingRequestReader).GetMethod("Aggregates", BindingFlags.NonPublic | BindingFlags.Instance);
        var sorted = typeof(FormMissingRequestReader).GetMethod("Sorted", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(aggregates);
        Assert.NotNull(sorted);
        return Assert.IsAssignableFrom<IQueryable>(sorted.Invoke(null, [aggregates.Invoke(new FormMissingRequestReader(db), []),
            new ListMissingFormTopicsRequest { Sort = sort }]));
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

    private static ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
}
