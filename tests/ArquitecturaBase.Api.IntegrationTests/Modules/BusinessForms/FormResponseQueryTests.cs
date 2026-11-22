using System.Reflection;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormResponseQueryTests
{
    [Fact]
    public void Choice_order_keeps_each_versions_labels_and_parameterizes_them()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader = new FormResponseReader(db, new FormRecordReader(db));
        var first = new CanonicalFormChoice(Guid.NewGuid(), Text("Anterior A", "Previous A"));
        var second = new CanonicalFormChoice(Guid.NewGuid(), Text("Opción B", "Choice B"));
        var field = new CanonicalFormField(Guid.NewGuid(), "choice", "choice", Text("Elección", "Choice"), null, false, null, [first, second]);
        var old = Version(1, field);
        var current = Version(2, field with { Options = [first with { Label = Text("Actual A", "Current A") }, second] });
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "TEST", current.Id, [old, current]);
        var ordering = typeof(FormResponseReader).GetMethod("Ordered", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var query = Assert.IsAssignableFrom<IQueryable<BusinessFormRecord>>(ordering.Invoke(reader,
            [db.Set<BusinessFormRecord>().Where(record => record.DefinitionId == definition.Id), definition,
                new ListFormRecordsRequest { Sort = "field", SortFieldId = field.FieldId, DisplayCulture = "en" }]));
        var sql = query.Take(20).Select(record => record.Id).ToQueryString();
        var statement = sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
        Assert.Contains("jsonb_array_elements", statement, StringComparison.Ordinal);
        Assert.Contains("VersionId", statement, StringComparison.Ordinal);
        Assert.Equal(1, statement.Split("FROM \"BusinessFormRecordValues\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("@p0", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("Current A", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("Choice B", statement, StringComparison.Ordinal);
        Assert.Contains("Current A", sql, StringComparison.Ordinal);
        Assert.Contains("Previous A", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_queries_keep_versions_from_the_definition_snapshot()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader = new FormResponseReader(db, new FormRecordReader(db));
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{}", "{\"format\":1,\"questions\":[]}", "es", ["es", "en"]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "TEST", version.Id, [version]);
        var filtering = typeof(FormResponseReader).GetMethod("Filter", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var query = Assert.IsAssignableFrom<IQueryable<BusinessFormRecord>>(filtering.Invoke(reader,
            [definition, new ListFormRecordsRequest(), null]));
        var sql = query.ToQueryString();
        Assert.Contains("\"VersionId\" = ANY", sql, StringComparison.Ordinal);
        Assert.Contains(version.Id.ToString(), sql, StringComparison.Ordinal);
    }

    // F2: the count of «Por resolver» goes from the form's runs to resolve, by their index (DefinitionId, RecordId), to the responses
    // of «Todas» (current, not voided), once per response; it does not look at every response of the form with an EXISTS.
    [Fact]
    public void The_count_of_responses_to_resolve_starts_from_the_forms_runs_and_joins_the_current_responses()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader = new FormResponseReader(db, new FormRecordReader(db));
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{}", "{\"format\":1,\"questions\":[]}", "es", ["es", "en"]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "TEST", version.Id, [version]);
        var method = typeof(FormResponseReader).GetMethod("UnresolvedRecordIds", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var sql = Assert.IsAssignableFrom<IQueryable<Guid>>(method.Invoke(reader, [definition])).ToQueryString();
        var statement = sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
        Assert.StartsWith("SELECT DISTINCT", statement, StringComparison.Ordinal);
        Assert.Contains("FROM \"BusinessFormActionRuns\"", statement, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN", statement, StringComparison.Ordinal);
        Assert.Contains("\"ReplacedByRecordId\" IS NULL", statement, StringComparison.Ordinal);
        Assert.Contains("\"VersionId\" = ANY", statement, StringComparison.Ordinal);
        Assert.Contains("'Failed'", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("EXISTS", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_hydration_selects_light_rows_without_repeating_version_text_per_record()
    {
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new FormRecordReader(db);
        var method=typeof(FormRecordReader).GetMethod("QueryRows",BindingFlags.NonPublic|BindingFlags.Instance);
        Assert.NotNull(method);
        var query=Assert.IsAssignableFrom<IQueryable>(method.Invoke(reader,[new[]{Guid.NewGuid()}]));
        var sql=query.ToQueryString();
        Assert.DoesNotContain("NameJson",sql,StringComparison.Ordinal);Assert.DoesNotContain("SchemaJson",sql,StringComparison.Ordinal);
        Assert.DoesNotContain("BusinessFormVersions",sql,StringComparison.Ordinal);
        Assert.Contains("ValuesJson",sql,StringComparison.Ordinal);Assert.Contains("VersionId",sql,StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Catalog_order_and_facets_use_parameterized_historical_labels_with_the_same_fallback(string culture)
    {
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new FormResponseReader(db,new FormRecordReader(db));var catalogId=Guid.NewGuid();
        var field=new CanonicalFormField(Guid.NewGuid(),"branch","choice",Text("Sucursal","Branch"),null,false,null,null,Source:new(catalogId));
        var version=Version(1,field);var definition=new FormResponseDefinitionRow(Guid.NewGuid(),"TEST",version.Id,[version]);
        var ordered=typeof(FormResponseReader).GetMethod("Ordered",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var records=Assert.IsAssignableFrom<IQueryable<BusinessFormRecord>>(ordered.Invoke(reader,[db.Set<BusinessFormRecord>(),definition,new ListFormRecordsRequest{Sort="field",SortFieldId=field.FieldId,DisplayCulture=culture}]));
        var catalogOptions=typeof(FormResponseReader).GetMethod("CatalogOptions",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var facets=Assert.IsAssignableFrom<IQueryable>(catalogOptions.Invoke(reader,[definition,field.FieldId,catalogId,culture,null]));
        foreach(var sql in new[]{records.Take(20).Select(record=>record.Id).ToQueryString(),facets.ToQueryString()})
        {
            var statement=sql[sql.IndexOf("SELECT",StringComparison.Ordinal)..];
            Assert.Contains("TranslationsJson",statement,StringComparison.Ordinal);Assert.Contains("COALESCE",statement,StringComparison.Ordinal);
            // Without a translation in the version's culture the label falls back to the other one, the item's original, never to Spanish.
            Assert.Contains("CASE presentation.value->>'culture' WHEN 'en' THEN 'es' ELSE 'en' END",statement,StringComparison.Ordinal);
            Assert.DoesNotContain("-> 'es' ->>",statement,StringComparison.Ordinal);
            Assert.Contains("RecordId",statement,StringComparison.Ordinal);Assert.Contains("FieldId",statement,StringComparison.Ordinal);Assert.Contains("ItemId",statement,StringComparison.Ordinal);
            Assert.Contains("@p0",statement,StringComparison.Ordinal);Assert.Contains(culture,sql,StringComparison.Ordinal);
            Assert.DoesNotContain("BusinessFormCatalogItems",statement,StringComparison.Ordinal);
        }
    }
    // OT1-C: the fixed columns of the sheet sort in SQL before its usual order, the newest number (unique, so EF does not repeat it as
    // the tiebreaker). A response passed from a document shows «—» as its channel and goes last; the state follows its circuit;
    // «Reglas», the actions to resolve.
    [Fact]
    public void The_fixed_columns_sort_in_sql_before_the_usual_order()
    {
        using var db = Database();
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "TEST", Guid.NewGuid(), [Version(1, new CanonicalFormField(Guid.NewGuid(), "name", "shortText", Text("Nombre", "Name"), null, false, null, null))]);
        string[] OrderBy(string sort) => OrderTerms(Ordered(db, definition, new ListFormRecordsRequest { Sort = sort }).Select(record => record.Id).ToQueryString());

        var corrected = OrderBy("-corrected");
        var channel = OrderBy("-channel");
        var ascendingChannel = OrderBy("channel");
        var state = OrderBy("state");
        var rules = OrderBy("-rules");

        Assert.Contains("\"ReplacesRecordId\" IS NOT NULL", corrected[0], StringComparison.Ordinal);
        Assert.EndsWith("DESC", corrected[0], StringComparison.Ordinal);
        Assert.EndsWith("IS NULL", channel[0], StringComparison.Ordinal);
        Assert.Contains("\"EnteredInApp\"", channel[1], StringComparison.Ordinal);
        Assert.Contains("FROM \"FormReceivedDocuments\"", channel[1], StringComparison.Ordinal);
        Assert.Contains("\"OriginalReceiptId\"", channel[1], StringComparison.Ordinal);
        Assert.EndsWith("DESC", channel[1], StringComparison.Ordinal);
        Assert.Contains("\"EnteredInApp\"", ascendingChannel[0], StringComparison.Ordinal);
        Assert.Contains("'Received' THEN 0", state[0], StringComparison.Ordinal);
        Assert.Contains("'Voided' THEN 4", state[0], StringComparison.Ordinal);
        Assert.Contains("FROM \"BusinessFormActionRuns\"", rules[0], StringComparison.Ordinal);
        Assert.EndsWith("DESC", rules[0], StringComparison.Ordinal);
        foreach (var terms in new[] { corrected, channel, ascendingChannel, state, rules })
            Assert.Equal("b.\"Number\" DESC", terms[^1]);
    }

    // A catalog field of a question of one option sorts by the value its response sealed (an option by its text in the version's
    // language, a decimal as a number) and a result by the latest value of its datum; the ids and keys go as parameters, and in a
    // DESC an empty value goes last.
    [Fact]
    public void A_catalog_field_and_a_result_sort_by_their_stored_values_with_parameters()
    {
        using var db = Database();
        var catalogField = Guid.NewGuid();
        var field = new CanonicalFormField(Guid.NewGuid(), "branch", "choice", Text("Sucursal", "Branch"), null, false, null, null, Source: new(Guid.NewGuid()));
        var version = Version(1, field);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "TEST", version.Id, [version]);
        var byField = Ordered(db, definition, new ListFormRecordsRequest { Sort = "-catalogField", SortFieldId = field.FieldId, SortCatalogFieldId = catalogField, DisplayCulture = "en" })
            .Select(record => record.Id).ToQueryString();
        var byResult = Ordered(db, definition, new ListFormRecordsRequest { Sort = "-result", SortResultId = "arca.invoice.total" })
            .Select(record => record.Id).ToQueryString();
        var fieldTerms = OrderTerms(byField);
        var resultTerms = OrderTerms(byResult);

        Assert.EndsWith("IS NULL", fieldTerms[0], StringComparison.Ordinal);
        Assert.Contains("\"FieldValuesJson\"", fieldTerms[1], StringComparison.Ordinal);
        Assert.Contains("to_jsonb", fieldTerms[1], StringComparison.Ordinal);
        Assert.Contains("::numeric", fieldTerms[1], StringComparison.Ordinal);
        Assert.Contains("CASE presentation.value->>'culture' WHEN 'en' THEN 'es' ELSE 'en' END", fieldTerms[1], StringComparison.Ordinal);
        Assert.EndsWith("DESC", fieldTerms[1], StringComparison.Ordinal);
        Assert.DoesNotContain(catalogField.ToString("D"), Statement(byField), StringComparison.Ordinal);
        Assert.Contains(catalogField.ToString("D"), byField, StringComparison.Ordinal);
        Assert.EndsWith("IS NULL", resultTerms[0], StringComparison.Ordinal);
        Assert.Contains("FROM \"BusinessFormRecordResults\"", resultTerms[1], StringComparison.Ordinal);
        Assert.Contains("\"RecordedAtUtc\" DESC", resultTerms[1], StringComparison.Ordinal);
        Assert.Contains("::numeric", resultTerms[1], StringComparison.Ordinal);
        Assert.EndsWith("DESC", resultTerms[1], StringComparison.Ordinal);
        Assert.DoesNotContain("arca.invoice", Statement(byResult), StringComparison.Ordinal);
        Assert.Contains("arca.invoice", byResult, StringComparison.Ordinal);
        foreach (var terms in new[] { fieldTerms, resultTerms })
            Assert.Equal("b.\"Number\" DESC", terms[^1]);
    }

    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
    private static IQueryable<BusinessFormRecord> Ordered(ApplicationDbContext db, FormResponseDefinitionRow definition, ListFormRecordsRequest request) =>
        Assert.IsAssignableFrom<IQueryable<BusinessFormRecord>>(typeof(FormResponseReader).GetMethod("Ordered", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(new FormResponseReader(db, new FormRecordReader(db)), [db.Set<BusinessFormRecord>().Where(record => record.DefinitionId == definition.Id), definition, request]));
    private static string Statement(string sql) => sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
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
    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
    private static FormResponseVersionRow Version(long number, CanonicalFormField field) => new(Guid.NewGuid(), number, "{}",
        JsonSerializer.Serialize(new FormVersionSchema(1, [field]), JsonSerializerOptions.Web), "es", ["es", "en"]);
}
