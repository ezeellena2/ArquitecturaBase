using System.Reflection;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormRecordNavigationQueryTests
{
    [Fact]
    public void Navigation_selects_only_public_record_ids_with_the_complete_filter_and_order()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader = new FormResponseReader(db, new FormRecordReader(db, new DataProtectionFormPublicationProtector(new EphemeralDataProtectionProvider())));
        var field = new CanonicalFormField(Guid.NewGuid(), "amount", "decimal", new Dictionary<string, string> { ["es"] = "Importe" }, null, false, null, null);
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{}", JsonSerializer.Serialize(new FormVersionSchema(1, [field]), JsonSerializerOptions.Web), "es", ["es"]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "TEST", version.Id, [version]);
        var request = new ListFormRecordsRequest { Page = 8, PageSize = 1, Sort = "field", SortFieldId = field.FieldId,
            State = "Received", ActorIds = [Guid.NewGuid()], Search = "private", Conditions = [new(field.FieldId, "gte", ["10"])] };
        var method = typeof(FormResponseReader).GetMethod("NavigationIds", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var query = Assert.IsAssignableFrom<IQueryable<Guid>>(method.Invoke(reader, [definition, request]));
        var sql = query.ToQueryString();
        var select = sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
        var projection = select[..select.IndexOf("FROM", StringComparison.Ordinal)].Trim();
        Assert.Equal("SELECT b.\"Id\"", projection);
        Assert.Contains("\"DefinitionId\"", select, StringComparison.Ordinal);
        Assert.Contains("\"VersionId\" = ANY", select, StringComparison.Ordinal);
        Assert.Contains("\"ActorUserId\"", select, StringComparison.Ordinal);
        Assert.Contains("\"State\"", select, StringComparison.Ordinal);
        Assert.Contains("ILIKE", select, StringComparison.Ordinal);
        Assert.Contains("\"DecimalValue\" >=", select, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", select, StringComparison.Ordinal);
        Assert.Contains("b.\"Number\"", select, StringComparison.Ordinal);
        Assert.DoesNotContain("\nLIMIT ", select, StringComparison.Ordinal);
        Assert.Contains("\nLIMIT ", query.Skip(1).Take(1).ToQueryString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\nOFFSET ", select, StringComparison.Ordinal);
        Assert.DoesNotContain("ValuesJson", select, StringComparison.Ordinal);
        Assert.DoesNotContain("SchemaJson", select, StringComparison.Ordinal);
        Assert.DoesNotContain("NameJson", select, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceReceiptId", select, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(1, 0)]
    [InlineData(3, -1)]
    [InlineData(0, -1)]
    public void Navigation_resolves_edges_and_position_in_the_complete_ordered_set(int count, int anchor)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        var recordId = anchor < 0 ? Guid.NewGuid() : ids[anchor];
        var method = typeof(FormResponseReader).GetMethod("ProjectNavigation", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = method.Invoke(null, [recordId, ids]);
        if (anchor < 0) { Assert.Null(result); return; }
        var row = Assert.IsType<FormRecordNavigationRow>(result);
        Assert.Equal(recordId, row.RecordId);
        Assert.Equal(anchor == 0 ? (Guid?)null : ids[anchor - 1], row.PreviousId);
        Assert.Equal(anchor == count - 1 ? (Guid?)null : ids[anchor + 1], row.NextId);
        Assert.Equal(anchor + 1, row.Position);
        Assert.Equal(count, row.TotalCount);
    }
}
