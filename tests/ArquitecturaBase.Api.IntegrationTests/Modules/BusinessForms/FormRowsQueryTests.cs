using System.Reflection;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>The SQL of «Filtros» over a block of rows, without opening a connection.</summary>
public sealed class FormRowsQueryTests
{
    [Fact]
    public void Conditions_on_questions_of_the_block_are_met_by_one_same_item_without_multiplying_records()
    {
        using var db = Db();
        var (definition, item, quantity) = Definition();
        var request = new ListFormRecordsRequest
        {
            Conditions = [new(item.FieldId, "in", [Guid.CreateVersion7().ToString("D")]), new(quantity.FieldId, "gte", ["200"])],
        };

        var statement = Statement(Filter(Reader(db), definition, request).Select(record => record.Id).ToQueryString());

        // The second condition is looked up in the row of the first, inside one EXISTS on the response.
        Assert.Equal(2, Occurrences(statement, "FROM \"BusinessFormRecordValues\""));
        Assert.Equal(1, Occurrences(statement, "\"Row\" = "));
        Assert.DoesNotContain("JOIN", statement, StringComparison.Ordinal);
    }

    private static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
    private static FormResponseReader Reader(ApplicationDbContext db) => new(db,
        new FormRecordReader(db, new DataProtectionFormPublicationProtector(new EphemeralDataProtectionProvider())));
    private static IQueryable<BusinessFormRecord> Filter(FormResponseReader reader, FormResponseDefinitionRow definition, ListFormRecordsRequest request)
    {
        var method = typeof(FormResponseReader).GetMethod("Filter", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IQueryable<BusinessFormRecord>>(method.Invoke(reader, [definition, request, null]));
    }
    private static (FormResponseDefinitionRow Definition, CanonicalFormField Item, CanonicalFormField Quantity) Definition()
    {
        var texts = new Dictionary<string, string> { ["es"] = "Pregunta", ["en"] = "Question" };
        var block = new CanonicalFormField(Guid.CreateVersion7(), "items", "group", texts, null, true, null, null, Rows: new(1, 10));
        var item = new CanonicalFormField(Guid.CreateVersion7(), "item", "choice", texts, null, true, null, null, new(Guid.CreateVersion7()), Repeated: true);
        var quantity = new CanonicalFormField(Guid.CreateVersion7(), "quantity", "decimal", texts, null, true, null, null, Repeated: true);
        var version = new FormResponseVersionRow(Guid.CreateVersion7(), 1, "{}",
            JsonSerializer.Serialize(new FormVersionSchema(1, [block, item, quantity]), JsonSerializerOptions.Web), "es", ["es"]);
        return (new(Guid.CreateVersion7(), "ROWS", version.Id, [version]), item, quantity);
    }
    private static string Statement(string sql) => sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
    private static int Occurrences(string text, string token) => text.Split(token, StringSplitOptions.None).Length - 1;
}
