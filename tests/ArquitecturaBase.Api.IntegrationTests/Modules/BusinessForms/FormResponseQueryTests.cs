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
    public void Choice_order_uses_one_value_subquery_and_parameterizes_the_latest_labels()
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
        Assert.Contains("CASE", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("EXISTS", statement, StringComparison.Ordinal);
        Assert.Equal(1, statement.Split("FROM \"BusinessFormRecordValues\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("@label", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("Current A", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("Choice B", statement, StringComparison.Ordinal);
        Assert.Contains("Current A", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Previous A", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_queries_keep_versions_from_the_definition_snapshot()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader = new FormResponseReader(db, new FormRecordReader(db));
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{}", "{\"format\":1,\"questions\":[]}");
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "TEST", version.Id, [version]);
        var filtering = typeof(FormResponseReader).GetMethod("Filter", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var query = Assert.IsAssignableFrom<IQueryable<BusinessFormRecord>>(filtering.Invoke(reader,
            [definition, new ListFormRecordsRequest(), null]));
        var sql = query.ToQueryString();
        Assert.Contains("\"VersionId\" = ANY", sql, StringComparison.Ordinal);
        Assert.Contains(version.Id.ToString(), sql, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
    private static FormResponseVersionRow Version(long number, CanonicalFormField field) => new(Guid.NewGuid(), number, "{}",
        JsonSerializer.Serialize(new FormVersionSchema(1, [field]), JsonSerializerOptions.Web));
}
