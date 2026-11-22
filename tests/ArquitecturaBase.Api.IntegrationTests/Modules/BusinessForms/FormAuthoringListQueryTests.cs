using System.Reflection;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// OT1-C: «Diseñar formularios» and «Catálogos» sort by every column with a datum, in SQL and in the same statement, without a
// query per row; these tests build it without a connection.
public sealed class FormAuthoringListQueryTests
{
    // «Estado» follows its circuit as the list names it, with the latest publication read once. A form without an active version
    // or without a name of who changed it goes last: an ascending sort already leaves it there in Postgres, a descending one adds
    // an «is null» term first. Equal values keep the usual order, the name.
    [Fact]
    public void Designs_sort_by_state_place_version_responses_and_author_with_empty_values_last()
    {
        using var db = Context();
        var reader = new FormDefinitionReader(db);
        string[] OrderBy(string sort) => OrderTerms(ListQuery(reader, new ListFormDefinitionsRequest { Sort = sort }).ToQueryString());

        var byState = OrderBy("displayState");
        var byPlace = OrderBy("-completedIn");
        var byVersion = OrderBy("activeVersion");
        var byVersionDescending = OrderBy("-activeVersion");
        var byResponses = OrderBy("-responseCount");
        var byAuthor = OrderBy("-lastChangedBy");

        Assert.Contains("'Uncertain' THEN 2", byState[0], StringComparison.Ordinal);
        Assert.Contains("'Published' THEN 3", byState[0], StringComparison.Ordinal);
        Assert.Contains("'Failed' THEN 4", byState[0], StringComparison.Ordinal);
        Assert.Contains("\"ActiveVersionId\" IS NULL THEN 0", byState[0], StringComparison.Ordinal);
        Assert.Single(byState[0].Split("\"BusinessFormPublicationOperations\"").Skip(1));
        Assert.EndsWith("\"DisplayNameEs\"", byState[1], StringComparison.Ordinal);
        Assert.Contains("\"AppOnly\"", byPlace[0], StringComparison.Ordinal);
        Assert.EndsWith("DESC", byPlace[0], StringComparison.Ordinal);
        Assert.Contains("\"Number\"", byVersion[0], StringComparison.Ordinal);
        Assert.EndsWith("\"DisplayNameEs\"", byVersion[1], StringComparison.Ordinal);
        Assert.EndsWith("IS NULL", byVersionDescending[0], StringComparison.Ordinal);
        Assert.Contains("\"Number\"", byVersionDescending[1], StringComparison.Ordinal);
        Assert.EndsWith("DESC", byVersionDescending[1], StringComparison.Ordinal);
        Assert.EndsWith("\"LastRecordNumber\" DESC", byResponses[0], StringComparison.Ordinal);
        Assert.EndsWith("\"DisplayNameEs\"", byResponses[1], StringComparison.Ordinal);
        Assert.EndsWith("IS NULL", byAuthor[0], StringComparison.Ordinal);
        Assert.Contains("\"DisplayName\"", byAuthor[1], StringComparison.Ordinal);
        Assert.EndsWith("DESC", byAuthor[1], StringComparison.Ordinal);
    }

    // «De dónde salen» follows the order its kinds were declared in (written by hand, from the base, from another system) although
    // they are saved as text; «Estado» puts the active catalogs first; a catalog of people shows no number of options and goes last
    // in both directions.
    [Fact]
    public void Catalogs_sort_by_origin_state_and_options_with_people_catalogs_last()
    {
        using var db = Context();
        var reader = new FormCatalogReader(db);
        string[] OrderBy(string sort) => OrderTerms(ListQuery(reader, new ListFormCatalogsRequest { Sort = sort }, true, true).ToQueryString());

        var byKind = OrderBy("kind");
        var byState = OrderBy("state");
        var byOptions = OrderBy("optionsCount");
        var byOptionsDescending = OrderBy("-optionsCount");

        Assert.Contains("'Manual' THEN 0", byKind[0], StringComparison.Ordinal);
        Assert.Contains("'Filters' THEN 1", byKind[0], StringComparison.Ordinal);
        Assert.Contains("'External' THEN 2", byKind[0], StringComparison.Ordinal);
        Assert.EndsWith("\"DisplayNameEs\"", byKind[1], StringComparison.Ordinal);
        Assert.StartsWith("NOT", byState[0], StringComparison.Ordinal);
        Assert.Contains("\"IsActive\"", byState[0], StringComparison.Ordinal);
        Assert.EndsWith("\"DisplayNameEs\"", byState[1], StringComparison.Ordinal);
        Assert.Contains("count(*)", byOptions[0], StringComparison.Ordinal);
        Assert.Contains("IN ('Manual', 'External')", byOptions[0], StringComparison.Ordinal);
        Assert.EndsWith("\"DisplayNameEs\"", byOptions[1], StringComparison.Ordinal);
        Assert.EndsWith("IS NULL", byOptionsDescending[0], StringComparison.Ordinal);
        Assert.EndsWith("DESC", byOptionsDescending[1], StringComparison.Ordinal);
    }

    private static IQueryable ListQuery(object reader, params object[] arguments)
    {
        var method = reader.GetType().GetMethod("ListQuery", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IQueryable>(method.Invoke(reader, arguments));
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
