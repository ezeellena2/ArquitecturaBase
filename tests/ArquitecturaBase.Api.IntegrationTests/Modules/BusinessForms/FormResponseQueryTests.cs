using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
using Microsoft.AspNetCore.DataProtection;
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
        var reader = new FormResponseReader(db, new FormRecordReader(db, new DataProtectionFormPublicationProtector(new EphemeralDataProtectionProvider())));
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
        var reader = new FormResponseReader(db, new FormRecordReader(db, new DataProtectionFormPublicationProtector(new EphemeralDataProtectionProvider())));
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{}", "{\"format\":1,\"questions\":[]}", "es", ["es", "en"]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "TEST", version.Id, [version]);
        var filtering = typeof(FormResponseReader).GetMethod("Filter", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var query = Assert.IsAssignableFrom<IQueryable<BusinessFormRecord>>(filtering.Invoke(reader,
            [definition, new ListFormRecordsRequest(), null]));
        var sql = query.ToQueryString();
        Assert.Contains("\"VersionId\" = ANY", sql, StringComparison.Ordinal);
        Assert.Contains(version.Id.ToString(), sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_hydration_selects_light_rows_without_repeating_version_text_per_record()
    {
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new FormRecordReader(db, new DataProtectionFormPublicationProtector(new EphemeralDataProtectionProvider()));
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
        var reader=new FormResponseReader(db,new FormRecordReader(db, new DataProtectionFormPublicationProtector(new EphemeralDataProtectionProvider())));var catalogId=Guid.NewGuid();
        var field=new CanonicalFormField(Guid.NewGuid(),"branch","choice",Text("Sucursal","Branch"),null,false,null,null,Source:new(catalogId));
        var version=Version(1,field);var definition=new FormResponseDefinitionRow(Guid.NewGuid(),"TEST",version.Id,[version]);
        var ordered=typeof(FormResponseReader).GetMethod("Ordered",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var records=Assert.IsAssignableFrom<IQueryable<BusinessFormRecord>>(ordered.Invoke(reader,[db.Set<BusinessFormRecord>(),definition,new ListFormRecordsRequest{Sort="field",SortFieldId=field.FieldId,DisplayCulture=culture}]));
        var catalogOptions=typeof(FormResponseReader).GetMethod("CatalogOptions",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var facets=Assert.IsAssignableFrom<IQueryable>(catalogOptions.Invoke(reader,[definition,field.FieldId,catalogId,culture]));
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
    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
    private static FormResponseVersionRow Version(long number, CanonicalFormField field) => new(Guid.NewGuid(), number, "{}",
        JsonSerializer.Serialize(new FormVersionSchema(1, [field]), JsonSerializerOptions.Web), "es", ["es", "en"]);
}
