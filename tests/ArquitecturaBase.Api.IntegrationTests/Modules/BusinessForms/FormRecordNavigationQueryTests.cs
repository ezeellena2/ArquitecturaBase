using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormRecordNavigationQueryTests
{
    [Fact]
    public void Navigation_returns_only_the_anchor_after_computing_the_complete_filtered_window()
    {
        using var db = Database();
        var reader = Reader(db);
        var field = Field("decimal");
        var definition = Definition(field);
        var request = new ListFormRecordsRequest { Page = 8, PageSize = 1, Sort = "field", SortFieldId = field.FieldId,
            State = "Received", ActorIds = [Guid.NewGuid()], Search = "private", Conditions = [new(field.FieldId, "gte", ["10"])] };
        var recordId = Guid.NewGuid();
        using var command = Navigation(reader, definition, recordId, request).CreateDbCommand();
        var sql = command.CommandText;

        Assert.Contains("ROW_NUMBER() OVER record_order", sql, StringComparison.Ordinal);
        Assert.Contains("LAG(b.\"Id\") OVER record_order", sql, StringComparison.Ordinal);
        Assert.Contains("LEAD(b.\"Id\") OVER record_order", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(*) OVER ()", sql, StringComparison.Ordinal);
        Assert.Contains("::integer", sql, StringComparison.Ordinal);
        Assert.Contains("WINDOW record_order AS (ORDER BY", sql, StringComparison.Ordinal);
        Assert.Contains("b.\"Number\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"DefinitionId\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"VersionId\" = ANY", sql, StringComparison.Ordinal);
        Assert.Contains("\"ActorUserId\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"State\"", sql, StringComparison.Ordinal);
        Assert.Contains("ILIKE", sql, StringComparison.Ordinal);
        Assert.Contains("\"DecimalValue\" >=", sql, StringComparison.Ordinal);
        Assert.True(sql.LastIndexOf("WHERE \"RecordId\" = @navigation_record_id", StringComparison.Ordinal)
            > sql.LastIndexOf("WINDOW record_order", StringComparison.Ordinal));
        Assert.Equal(recordId, command.Parameters["navigation_record_id"].Value);
        foreach (var excluded in new[] { "ValuesJson", "SchemaJson", "NameJson", "SourceReceiptId", "OFFSET" })
            Assert.DoesNotContain(excluded, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ROW_NUMBER() OVER ()", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("private", sql, StringComparison.Ordinal);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("number")]
    [InlineData("submittedAtUtc")]
    [InlineData("decimal")]
    [InlineData("date")]
    [InlineData("boolean")]
    [InlineData("shortText")]
    [InlineData("choice")]
    [InlineData("catalog")]
    [InlineData("photo")]
    [InlineData("actor")]
    public void Navigation_preserves_the_complete_sheet_order_and_typed_parameters(string type)
    {
        using var db = Database();
        var reader = Reader(db);
        var builtIn = type is "default" or "number" or "submittedAtUtc" or "actor";
        var field = Field(builtIn ? "shortText" : type);
        var definition = Definition(field);
        foreach (var descending in new[] { false, true })
        {
            var request = new ListFormRecordsRequest { Sort = type == "default" ? null : (descending ? "-" : "") + (builtIn ? type : "field"),
                SortFieldId = builtIn ? null : field.FieldId, Search = "private ' ORDER BY FROM ) --" };
            var ids = typeof(FormResponseReader).GetMethod("NavigationIds", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var source = Assert.IsAssignableFrom<IQueryable<Guid>>(ids.Invoke(reader, [definition, request]));
            using var original = source.CreateDbCommand();
            using var bounded = Navigation(reader, definition, Guid.NewGuid(), request).CreateDbCommand();
            var order = original.CommandText[(original.CommandText.LastIndexOf("\nORDER BY ", StringComparison.Ordinal) + 10)..].Trim();
            Assert.Contains("WINDOW record_order AS (ORDER BY " + order + ")", bounded.CommandText, StringComparison.Ordinal);
            Assert.Equal(original.Parameters.Count + 1, bounded.Parameters.Count);
            foreach (DbParameter parameter in original.Parameters)
            {
                var copied = Assert.IsType<NpgsqlParameter>(bounded.Parameters[parameter.ParameterName]);
                Assert.NotSame(parameter, copied);
                Assert.Equal(parameter.Value, copied.Value);
                Assert.Equal(Assert.IsType<NpgsqlParameter>(parameter).NpgsqlDbType, copied.NpgsqlDbType);
            }
            Assert.DoesNotContain(request.Search, bounded.CommandText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Navigation_preserves_literal_braces_when_the_compiled_command_reenters_ef()
    {
        using var db = Database();
        var reader = Reader(db);
        var definitionId = Guid.NewGuid();
        var ids = db.Set<BusinessFormRecord>().FromSqlRaw("""
            SELECT * FROM "BusinessFormRecords"
            WHERE "State" <> '{{literal}} {{0}}' AND "DefinitionId" = @definition
            """, new NpgsqlParameter("definition", definitionId))
            .OrderBy(record => record.Number).Select(record => record.Id);
        using var original = ids.CreateDbCommand();
        using var bounded = Bounded(reader, ids, Guid.NewGuid()).CreateDbCommand();
        Assert.Contains("'{literal} {0}'", original.CommandText, StringComparison.Ordinal);
        Assert.Contains("'{literal} {0}'", bounded.CommandText, StringComparison.Ordinal);
        var parameter = Assert.IsType<NpgsqlParameter>(bounded.Parameters["definition"]);
        Assert.Equal(definitionId, parameter.Value);
        Assert.Equal(Assert.IsType<NpgsqlParameter>(original.Parameters["definition"]).NpgsqlDbType, parameter.NpgsqlDbType);
    }

    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
    private static FormResponseReader Reader(ApplicationDbContext db) => new(db,
        new FormRecordReader(db, new DataProtectionFormPublicationProtector(new EphemeralDataProtectionProvider())));
    private static CanonicalFormField Field(string type) => new(Guid.NewGuid(), "value", type == "catalog" ? "choice" : type,
        new Dictionary<string, string> { ["es"] = "Valor" }, null, false, null,
        type == "choice" ? [new(Guid.NewGuid(), new Dictionary<string, string> { ["es"] = "Opción" })] : null,
        Source: type == "catalog" ? new(Guid.NewGuid()) : null);
    private static FormResponseDefinitionRow Definition(CanonicalFormField field)
    {
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{}",
            JsonSerializer.Serialize(new FormVersionSchema(1, [field]), JsonSerializerOptions.Web), "es", ["es"]);
        return new(Guid.NewGuid(), "TEST", version.Id, [version]);
    }
    private static IQueryable Navigation(FormResponseReader reader, FormResponseDefinitionRow definition,
        Guid recordId, ListFormRecordsRequest request)
    {
        var method = typeof(FormResponseReader).GetMethod("NavigationIds", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Bounded(reader, Assert.IsAssignableFrom<IQueryable<Guid>>(method.Invoke(reader, [definition, request])), recordId);
    }
    private static IQueryable Bounded(FormResponseReader reader, IQueryable<Guid> ids, Guid recordId)
    {
        var method = typeof(FormResponseReader).GetMethod("NavigationQuery", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IQueryable>(method.Invoke(reader, [ids, recordId]));
    }
}
