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

public sealed class FormMultipleChoiceQueryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Any_of_multiple_choices_is_one_correlated_exists_without_multiplying_records(bool catalog)
    {
        using var db = Db();
        var (definition, field, selected, _) = Definition(catalog);
        var request = new ListFormRecordsRequest { Conditions = [new(field.FieldId, "anyOf", selected.Select(id => id.ToString("D")).ToArray())] };
        var sql = Filter(Reader(db), definition, request).Select(record => record.Id).ToQueryString();
        var statement = Statement(sql);
        Assert.Contains("EXISTS", statement, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(statement, "FROM \"BusinessFormRecordValues\""));
        Assert.Contains("\"ChoiceId\" = ANY", statement, StringComparison.Ordinal);
        Assert.Contains("\"RecordId\"", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("JOIN", statement, StringComparison.Ordinal);
        foreach (var id in selected)
        {
            Assert.Contains(id.ToString("D"), sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(id.ToString("D"), statement, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("answered")]
    [InlineData("unanswered")]
    [InlineData("notAsked")]
    public void Presence_keeps_the_same_record_semantics_and_unanswered_still_includes_not_asked(string operation)
    {
        using var db = Db();
        var (definition, field, _, missingVersionId) = Definition(false);
        var request = new ListFormRecordsRequest { Conditions = [new(field.FieldId, operation, null)] };
        var sql = Filter(Reader(db), definition, request).Select(record => record.Id).ToQueryString();
        var statement = Statement(sql);
        Assert.Contains(missingVersionId.ToString("D"), sql, StringComparison.OrdinalIgnoreCase);
        if (operation != "notAsked") Assert.Equal(1, Occurrences(statement, "\"VersionId\""));
        if (operation == "notAsked")
        {
            Assert.True(Occurrences(statement, "\"VersionId\"") >= 2);
            Assert.True(statement.Contains("NOT", StringComparison.Ordinal) || statement.Contains("<>", StringComparison.Ordinal));
            Assert.DoesNotContain("BusinessFormRecordValues", statement, StringComparison.Ordinal);
            Assert.DoesNotContain("BusinessFormRecordFiles", statement, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("BusinessFormRecordValues", statement, StringComparison.Ordinal);
            Assert.Contains("BusinessFormRecordFiles", statement, StringComparison.Ordinal);
            Assert.Contains(operation == "unanswered" ? "NOT EXISTS" : "EXISTS", statement, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_choices_facet_ignores_its_own_any_of_condition_but_keeps_other_filters()
    {
        using var db = Db();
        var (definition, field, selected, _) = Definition(false);
        var actor = Guid.CreateVersion7();
        var request = new ListFormRecordsRequest
        {
            ActorIds = [actor], Conditions = [new(field.FieldId, "anyOf", selected.Select(id => id.ToString("D")).ToArray())],
        };
        var sql = Filter(Reader(db), definition, request, field.FieldId).Select(record => record.Id).ToQueryString();
        var statement = Statement(sql);
        Assert.Contains("ActorUserId", statement, StringComparison.Ordinal);
        Assert.Contains(actor.ToString("D"), sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BusinessFormRecordValues", statement, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Multiple_catalog_options_keep_captured_labels_in_the_whole_published_culture(string culture)
    {
        using var db = Db();
        var (definition, field, _, _) = Definition(true);
        var method = typeof(FormResponseReader).GetMethod("CatalogOptions", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(Reader(db), [definition, field.FieldId, field.Source!.Catalog!.Value, culture]));
        var sql = query.ToQueryString();
        var statement = Statement(sql);
        Assert.Contains("BusinessFormRecordReferences", statement, StringComparison.Ordinal);
        Assert.Contains("TranslationsJson", statement, StringComparison.Ordinal);
        Assert.Contains("COALESCE", statement, StringComparison.Ordinal);
        Assert.Contains("GROUP BY", statement, StringComparison.Ordinal);
        Assert.Contains("\"culture\":\"es\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"culture\":\"en\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("BusinessFormCatalogItems", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("BusinessFormCatalogs", statement, StringComparison.Ordinal);
    }

    private static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
    private static FormResponseReader Reader(ApplicationDbContext db) => new(db,
        new FormRecordReader(db, new DataProtectionFormPublicationProtector(new EphemeralDataProtectionProvider())));
    private static IQueryable<BusinessFormRecord> Filter(FormResponseReader reader, FormResponseDefinitionRow definition,
        ListFormRecordsRequest request, Guid? ignoredField = null)
    {
        var method = typeof(FormResponseReader).GetMethod("Filter", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IQueryable<BusinessFormRecord>>(method.Invoke(reader, [definition, request, ignoredField]));
    }
    private static (FormResponseDefinitionRow Definition, CanonicalFormField Field, Guid[] Choices, Guid MissingVersionId) Definition(bool catalog)
    {
        var fieldId = Guid.CreateVersion7(); var selected = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        var texts = new Dictionary<string, string> { ["es"] = "Opciones", ["en"] = "Options" };
        var field = new CanonicalFormField(fieldId, "field_" + fieldId.ToString("N"), "multiChoice", texts, null, false, null,
            catalog ? null : selected.Select(id => new CanonicalFormChoice(id, texts)).ToArray(),
            catalog ? new(Guid.CreateVersion7()) : null, Selection: new(0, 2));
        var old = new FormResponseVersionRow(Guid.CreateVersion7(), 1, "{}", JsonSerializer.Serialize(new FormVersionSchema(1, []), JsonSerializerOptions.Web), "es", ["es"]);
        var current = new FormResponseVersionRow(Guid.CreateVersion7(), 2, "{}", JsonSerializer.Serialize(new FormVersionSchema(1, [field]), JsonSerializerOptions.Web), "es", ["es"]);
        return (new(Guid.CreateVersion7(), "MULTI", current.Id, [old, current]), field, selected, old.Id);
    }
    private static string Statement(string sql) => sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
    private static int Occurrences(string text, string token) => text.Split(token, StringSplitOptions.None).Length - 1;
}
