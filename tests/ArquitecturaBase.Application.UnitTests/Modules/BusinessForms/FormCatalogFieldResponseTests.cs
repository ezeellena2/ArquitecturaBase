using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogFieldResponseTests
{
    [Theory]
    [InlineData("es", "Etiqueta histórica", "Centro")]
    [InlineData("en", "Historical label", "Center")]
    public void Details_project_all_sealed_fields_with_exact_values_and_localized_names(string culture, string label, string choiceText)
    {
        var fixture = Fixture();
        var projected = FormRecordPresentationPolicy.Project(fixture.Record, culture, fixture.Schema);
        var choice = Assert.IsType<StructuredRecordChoiceValue>(Assert.Single(projected.Values).Value);
        var json = JsonSerializer.SerializeToElement(choice, JsonSerializerOptions.Web);
        Assert.True(json.TryGetProperty("fields", out var fields));
        Assert.Equal(7, fields.GetArrayLength());
        Assert.Equal(label, fields[0].GetProperty("label").GetString());
        Assert.Equal("data", fields[0].GetProperty("role").GetString());
        Assert.Equal("123456789012345678", fields[1].GetProperty("value").GetString());
        Assert.Equal("0.1234567890123456789012345678", fields[2].GetProperty("value").GetString());
        Assert.Equal("2026-10-23", fields[3].GetProperty("value").GetString());
        Assert.Equal("false", fields[4].GetProperty("value").GetString());
        Assert.Equal("filter", fields[5].GetProperty("role").GetString());
        Assert.Equal(choiceText, fields[5].GetProperty("text").GetString());
        Assert.Equal("Nombre sin traducir", fields[6].GetProperty("label").GetString());
    }

    [Fact]
    public void A_response_before_extra_fields_omits_the_optional_fields_member()
    {
        var fixture = Fixture();
        var reference = fixture.Record.References![0] with { FieldValuesJson = null };
        var projected = FormRecordPresentationPolicy.Project(fixture.Record with { References = [reference] }, "en", fixture.Schema);
        var choice = Assert.Single(projected.Values).Value;
        Assert.False(JsonSerializer.SerializeToElement(choice, JsonSerializerOptions.Web).TryGetProperty("fields", out _));
    }

    [Fact]
    public void Multiple_selections_keep_their_own_sealed_fields_in_the_received_order()
    {
        var fixture = Fixture();
        var question = fixture.Schema.Questions[0] with { Type = "multiChoice", Selection = new(0, 3) };
        var second = fixture.Record.References![0] with { ItemId = Guid.NewGuid(), Text = "Second",
            FieldValuesJson = JsonSerializer.Serialize(new[] { Captured(fixture.ExtraIds[0], "text", "data", JsonSerializer.SerializeToElement("second")) }, JsonSerializerOptions.Web) };
        var record = fixture.Record with
        {
            References = [fixture.Record.References[0], second],
            ValuesJson = JsonSerializer.Serialize(new[] { new { fieldId = question.FieldId, value = new[] { second.ItemId, fixture.Record.References[0].ItemId } } }),
        };
        var projected = FormRecordPresentationPolicy.Project(record, "en", new(1, [question]));
        var choices = Assert.IsAssignableFrom<IReadOnlyList<StructuredRecordChoiceValue>>(Assert.Single(projected.Values).Value);
        Assert.Equal(new[] { "Second", "Original choice" }, choices.Select(choice => choice.Label));
        var json = JsonSerializer.SerializeToElement(choices, JsonSerializerOptions.Web);
        Assert.Equal("second", json[0].GetProperty("fields")[0].GetProperty("value").GetString());
        Assert.Equal("sealed", json[1].GetProperty("fields")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task The_chat_adapter_keeps_the_existing_choice_contract_without_extra_fields()
    {
        var fixture = Fixture();
        var version = new FormResponseVersionRow(fixture.Record.VersionId, 1, fixture.Record.NameJson, fixture.Record.SchemaJson, "es", ["es", "en"]);
        var reader = new FakeFormRecordReadBatch { Batch = new([fixture.Record], new Dictionary<Guid, FormResponseVersionRow> { [version.Id] = version }) };
        var permissions = new FakePermissionService(); var actor = Guid.NewGuid();
        permissions.Permissions[actor] = [BusinessFormsPermissions.RecordsRead];
        var source = new BusinessFormRecordSource(reader, permissions);
        var reply = await source.ReadAsync(actor, "en", [fixture.Record.SourceReceiptId], TestContext.Current.CancellationToken);
        var choice = Assert.Single(Assert.Single(reply.Replies).Value.Values).Value;
        var json = JsonSerializer.SerializeToElement(choice, JsonSerializerOptions.Web);
        Assert.Equal("Original choice", json.GetProperty("label").GetString());
        Assert.False(json.TryGetProperty("fields", out _));
        Assert.Equal(1, reader.Reads);
    }

    [Fact]
    public void Sheet_headers_keep_extra_field_metadata_per_catalog_without_creating_sortable_questions()
    {
        var fixture = Fixture(); var question = fixture.Schema.Questions[0]; var catalog = question.Source!.Catalog!.Value;
        var version = new FormResponseVersionRow(fixture.Record.VersionId, 1, fixture.Record.NameJson, fixture.Record.SchemaJson, "es", ["es"]);
        var extra = new[] { new FormResponseCatalogFieldRow(fixture.ExtraIds[0], "code", Text("Código", "Code"), "text", "data"),
            new FormResponseCatalogFieldRow(fixture.ExtraIds[1], "zone", new Dictionary<string, string> { ["es"] = "Zona" }, "catalog", "filter") };
        var definition = new FormResponseDefinitionRow(fixture.Record.DefinitionId, "F", version.Id, [version],
            CatalogFields: new Dictionary<Guid, IReadOnlyList<FormResponseCatalogFieldRow>> { [catalog] = extra });
        var column = Assert.Single(FormResponsePresentationPolicy.Columns(definition, "en"));
        var json = JsonSerializer.SerializeToElement(column, JsonSerializerOptions.Web);
        Assert.Equal("Proveedor", column.Label);
        Assert.Equal(new[] { "Código", "Zona" }, json.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("label").GetString()));
        Assert.Equal(new[] { "data", "filter" }, json.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("role").GetString()));
        var sorted = FormRecordFilterPolicy.Validate(new ListFormRecordsRequest { Sort = "field", SortFieldId = extra[0].Id }, definition);
        Assert.True(sorted.IsFailure);
        var filtered = FormRecordFilterPolicy.Validate(new ListFormRecordsRequest { Conditions = [new(extra[0].Id, "equals", ["private"])] }, definition);
        Assert.True(filtered.IsFailure);
    }

    private static (FormRecordStoredRow Record, FormVersionSchema Schema, Guid[] ExtraIds) Fixture()
    {
        var catalog = Guid.NewGuid(); var item = Guid.NewGuid(); var record = Guid.NewGuid();
        var question = new CanonicalFormField(Guid.NewGuid(), "supplier", "choice", Text("Proveedor", "Supplier"), null, false, null, null, Source: new(catalog));
        var schema = new FormVersionSchema(1, [question]); var extraIds = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();
        var fields = new[]
        {
            Captured(extraIds[0], "text", "data", JsonSerializer.SerializeToElement("sealed")),
            Captured(extraIds[1], "integer", "data", JsonSerializer.SerializeToElement(123456789012345678L)),
            Captured(extraIds[2], "decimal", "data", JsonSerializer.SerializeToElement("0.1234567890123456789012345678")),
            Captured(extraIds[3], "date", "data", JsonSerializer.SerializeToElement("2026-10-23")),
            Captured(extraIds[4], "boolean", "data", JsonSerializer.SerializeToElement(false)),
            Captured(extraIds[5], "catalog", "filter", JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("D"))) with { Text = Text("Centro", "Center") },
            Captured(extraIds[6], "text", "data", JsonSerializer.SerializeToElement("old")) with { Name = new Dictionary<string, string> { ["es"] = "Nombre sin traducir" } },
        };
        var reference = new FormRecordReferenceRow(record, question.FieldId, catalog, 1, item, "Original choice", null, "Suppliers",
            FieldValuesJson: JsonSerializer.Serialize(fields, JsonSerializerOptions.Web));
        return (new(Guid.NewGuid(), record, 1, Guid.NewGuid(), "F", Guid.NewGuid(), 1,
            JsonSerializer.Serialize(Text("Formulario", "Form")), JsonSerializer.Serialize(schema, JsonSerializerOptions.Web),
            JsonSerializer.Serialize(new[] { new { fieldId = question.FieldId, value = item } }), [reference]), schema, extraIds);
    }

    private static FormCatalogCapturedField Captured(Guid id, string type, string role, JsonElement value) =>
        new(id, "field_" + id.ToString("N"), Text("Etiqueta histórica", "Historical label"), type, role, value);
    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
}
