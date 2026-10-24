using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormMultipleChoiceReadPolicyTests
{
    [Theory]
    [InlineData(false, "es", "Ruido", "Frío")]
    [InlineData(false, "en", "Noise", "Cold")]
    [InlineData(true, "es", "Ruido histórico", "Frío histórico")]
    [InlineData(true, "en", "Historical noise", "Historical cold")]
    public void Selections_follow_the_captured_order_and_use_their_sealed_labels(bool catalog, string culture, string first, string second)
    {
        var (field, row, _) = Record(catalog);
        var reply = FormRecordPresentationPolicy.Project(row, culture, new(1, [field]));
        var answer = Assert.Single(reply.Values);
        Assert.Equal("multiChoice", answer.Type);
        var choices = Assert.IsAssignableFrom<IReadOnlyList<StructuredRecordChoiceValue>>(answer.Value);
        Assert.Equal(new[] { secondId.ToString("D"), firstId.ToString("D") }, choices.Select(choice => choice.ChoiceId));
        Assert.Equal(new[] { first, second }, choices.Select(choice => choice.Label));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(answer, JsonSerializerOptions.Web));
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("value").ValueKind);
        Assert.Equal(first, json.RootElement.GetProperty("value")[0].GetProperty("label").GetString());
        if (catalog)
        {
            Assert.Equal(new[] { "0", "1.25" }, choices.Select(choice => choice.Value));
            Assert.All(choices, choice => Assert.Equal("Decimal", choice.ValueType));
        }
        else Assert.All(choices, choice => Assert.Null(choice.CatalogId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_and_omitted_optional_selections_are_unanswered_without_becoming_not_asked(bool explicitEmpty)
    {
        var (field, row, _) = Record(false);
        row = row with { ValuesJson = explicitEmpty ? JsonSerializer.Serialize(new[] { new { fieldId = field.FieldId, value = Array.Empty<Guid>() } }) : "[]" };
        var reply = FormRecordPresentationPolicy.Project(row, "es", new(1, [field]));
        var column = new FormResponseColumn(field.FieldId, "multiChoice", "Fallas", 1, []);
        Assert.Equal("Unanswered", Assert.Single(FormResponsePresentationPolicy.Cells([column], reply)).State);
        Assert.Equal("NotAsked", Assert.Single(FormResponsePresentationPolicy.Cells([column], reply with { Values = [] })).State);
        var snapshot = Snapshot(row);
        Assert.False(FormRecordConditionEvaluator.Matches([new(field.FieldId, "answered", null)], snapshot));
        Assert.True(FormRecordConditionEvaluator.Matches([new(field.FieldId, "unanswered", null)], snapshot));
        Assert.False(FormRecordConditionEvaluator.Matches([new(field.FieldId, "notAsked", null)], snapshot));
    }

    [Theory]
    [InlineData("anyOf", true)]
    [InlineData("in", false)]
    [InlineData("equals", false)]
    public void Multiple_selection_uses_an_explicit_any_of_filter(string operation, bool valid)
    {
        var (field, row, _) = Record(false);
        var version = new FormResponseVersionRow(row.VersionId, 1, row.NameJson, row.SchemaJson, "es", ["es", "en"]);
        var definition = new FormResponseDefinitionRow(row.DefinitionId, row.Prefix, version.Id, [version]);
        Assert.Equal(valid, FormRecordFilterPolicy.Validate(new() { Conditions = [new(field.FieldId, operation, [firstId.ToString("D"), secondId.ToString("D")])] }, definition).IsSuccess);
        Assert.True(FormRecordFilterPolicy.Validate(new() { Conditions = [new(field.FieldId, "anyOf", [])] }, definition).IsFailure);
        Assert.True(FormRecordFilterPolicy.Validate(new() { Conditions = [new(field.FieldId, "anyOf", ["invalid"])] }, definition).IsFailure);
        foreach (var sort in new[] { "field", "-field" })
            Assert.True(FormRecordFilterPolicy.Validate(new() { Sort = sort, SortFieldId = field.FieldId }, definition).IsFailure);
    }

    [Fact]
    public void Any_of_matches_an_intersection_without_treating_the_whole_array_as_one_guid()
    {
        var (field, row, _) = Record(false);
        var snapshot = Snapshot(row);
        Assert.True(FormRecordConditionEvaluator.Matches([new(field.FieldId, "anyOf", [firstId.ToString("D")])], snapshot));
        Assert.True(FormRecordConditionEvaluator.Matches([new(field.FieldId, "anyOf", [firstId.ToString("D"), secondId.ToString("D")])], snapshot));
        Assert.False(FormRecordConditionEvaluator.Matches([new(field.FieldId, "anyOf", [Guid.NewGuid().ToString("D")])], snapshot));
    }

    [Theory]
    [InlineData("es", "Ruido; Frío", "Respuesta: Ruido, Frío")]
    [InlineData("en", "Noise; Cold", "Answer: Noise, Cold")]
    public void Excel_and_messages_render_the_ordered_labels_instead_of_the_collection_type(string culture, string excel, string message)
    {
        var (field, row, _) = Record(false);
        var reply = FormRecordPresentationPolicy.Project(row, culture, new(1, [field]));
        var cell = new FormResponseCell(field.FieldId, "multiChoice", "Answered", Assert.Single(reply.Values).Value);
        var workbook = FormExportPolicy.Cell(cell);
        Assert.Equal("text", workbook.Kind); Assert.Equal(excel, workbook.Value);
        var action = new FormRuleAction(Guid.NewGuid(), "NotifyAuthor", null, Text("Respuesta: {Fallas}", "Answer: {Issues}"), false);
        Assert.Equal(message, FormActionMessagePolicy.Render(action, new(1, [field]), Snapshot(row), culture));
    }

    [Fact]
    public void A_version_without_the_question_keeps_the_existing_not_asked_and_unanswered_filter_overlap()
    {
        var (field, row, _) = Record(false);
        row = row with { SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [])), ValuesJson = "[]" };
        var reply = FormRecordPresentationPolicy.Project(row, "es", new(1, []));
        var column = new FormResponseColumn(field.FieldId, "multiChoice", "Fallas", 1, []);
        Assert.Equal("NotAsked", Assert.Single(FormResponsePresentationPolicy.Cells([column], reply)).State);
        Assert.False(FormRecordConditionEvaluator.Matches([new(field.FieldId, "answered", null)], Snapshot(row)));
        Assert.True(FormRecordConditionEvaluator.Matches([new(field.FieldId, "unanswered", null)], Snapshot(row)));
        Assert.True(FormRecordConditionEvaluator.Matches([new(field.FieldId, "notAsked", null)], Snapshot(row)));
    }

    [Theory]
    [InlineData("es", "Histórica")]
    [InlineData("en", "Historical")]
    public async Task Rule_options_include_multiple_catalog_questions_and_read_a_shared_catalog_once(string culture, string label)
    {
        var fixture = new FormRuleFixture(); var catalog = Guid.NewGuid();
        var field = new CanonicalFormField(Guid.NewGuid(), "risks", "multiChoice", Text("Riesgos", "Risks"), null, false, null, null,
            Source: new(catalog), Selection: new(0, 3));
        fixture.Catalogs[catalog] = new(catalog, "risk", "{}", "{}", "es", "Manual", 1, true, default, null, null, null, 2);
        fixture.ManualItems[catalog] = [new(Guid.NewGuid(), "RISK", new Dictionary<string, string?> { ["es"] = "Histórica", ["en"] = "Historical" }, new Dictionary<string, string?>(), 0, false, null,
            new Dictionary<string, string>(), new Dictionary<string, IReadOnlyDictionary<string, string>>())];
        var result = await new FormConditionChoicePolicy(fixture, fixture).ReadAsync(fixture.User.UserId!.Value,
            [field, field with { FieldId = Guid.NewGuid(), Key = "other" }], culture, TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Count);
        Assert.All(result.Values, choices => Assert.Equal(label, Assert.Single(choices).Label));
        Assert.Equal(1, fixture.ManualAllItemReads); Assert.Equal(0, fixture.ManualPageReads);
        Assert.Equal(0, fixture.UnitOfWork.Transactions);
    }
    private static readonly Guid firstId = Guid.Parse("01990000-0000-7000-8000-000000000741");
    private static readonly Guid secondId = Guid.Parse("01990000-0000-7000-8000-000000000742");
    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
    private static FormActionRecordSnapshot Snapshot(FormRecordStoredRow row) => new(new(row,
        new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc), new(Guid.NewGuid(), "Author")), "es");
    private static (CanonicalFormField Field, FormRecordStoredRow Row, Guid CatalogId) Record(bool catalog)
    {
        var fieldId = Guid.NewGuid(); var recordId = Guid.NewGuid(); var catalogId = Guid.NewGuid();
        var field = new CanonicalFormField(fieldId, "issues", "multiChoice", Text("Fallas", "Issues"), null, false, null,
            catalog ? null : [new(firstId, Text("Frío", "Cold")), new(secondId, Text("Ruido", "Noise"))],
            Source: catalog ? new(catalogId) : null, Selection: new(0, 3));
        FormRecordReferenceRow Reference(Guid id, string es, string en, string value) => new(recordId, fieldId, catalogId, 3, id,
            es, "Detalle histórico", "Catálogo histórico", JsonSerializer.Serialize(new Dictionary<string, object>
            { ["es"] = new { text = es, detail = "Detalle histórico" }, ["en"] = new { text = en, detail = "Historical detail" } }), value, "Decimal");
        // Reverse reference storage order to require the actual ValuesJson selection order.
        var references = catalog ? new[] { Reference(firstId, "Frío histórico", "Historical cold", "1.25"), Reference(secondId, "Ruido histórico", "Historical noise", "0") } : [];
        var row = new FormRecordStoredRow(Guid.NewGuid(), recordId, 1, Guid.NewGuid(), "MULTI", Guid.NewGuid(), 1,
            JsonSerializer.Serialize(Text("Formulario", "Form")), JsonSerializer.Serialize(new FormVersionSchema(1, [field]), JsonSerializerOptions.Web),
            JsonSerializer.Serialize(new[] { new { fieldId, value = new[] { secondId, firstId } } }), references);
        return (field, row, catalogId);
    }
}
