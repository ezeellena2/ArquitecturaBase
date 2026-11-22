using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormRecordFilterPolicyTests
{
    [Theory]
    [InlineData("decimal", "gte", "12.50", true)]
    [InlineData("decimal", "gte", "12,50", false)]
    [InlineData("decimal", "contains", "12", false)]
    [InlineData("date", "lte", "2026-10-16", true)]
    [InlineData("date", "gte", "2026-02-31", false)]
    [InlineData("shortText", "contains", "private", true)]
    [InlineData("longText", "equals", "", false)]
    [InlineData("boolean", "in", "false", true)]
    [InlineData("boolean", "gte", "true", false)]
    [InlineData("choice", "in", "invalid", false)]
    [InlineData("photo", "answered", null, true)]
    [InlineData("document", "contains", "pdf", false)]
    public void Operators_and_values_match_the_question_type(string type, string operation, string? value, bool valid)
    {
        var question = new CanonicalFormField(Guid.NewGuid(), "question", type, new Dictionary<string,string> { ["es"] = "Pregunta", ["en"] = "Question" }, null, false, null, null);
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{}", JsonSerializer.Serialize(new FormVersionSchema(1, [question]), JsonSerializerOptions.Web), "es", ["es", "en"]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "T", version.Id, [version]);
        var request = new ListFormRecordsRequest { Conditions = [new(question.FieldId, operation, value is null ? null : [value])] };
        Assert.Equal(valid, FormRecordFilterPolicy.Validate(request, definition).IsSuccess);
    }
    [Theory]
    [InlineData("number")]
    [InlineData("-number")]
    [InlineData("field")]
    public void Unknown_sort_fields_are_rejected_for_every_sort_order(string sort)
    {
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{}", JsonSerializer.Serialize(new FormVersionSchema(1, []), JsonSerializerOptions.Web), "es", ["es", "en"]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "T", version.Id, [version]);
        var result = FormRecordFilterPolicy.Validate(new() { Sort = sort, SortFieldId = Guid.NewGuid() }, definition);
        Assert.True(result.IsFailure);
        Assert.Equal("Forms.Record.FilterInvalid", result.Error.Code);
    }
    [Theory]
    [InlineData("field ")]
    [InlineData(" field")]
    [InlineData(" -field ")]
    [InlineData("- field")]
    public void A_normalized_field_sort_always_requires_a_known_question(string sort)
    {
        var version=new FormResponseVersionRow(Guid.NewGuid(),1,"{}",JsonSerializer.Serialize(new FormVersionSchema(1,[]),JsonSerializerOptions.Web), "es", ["es", "en"]);
        var definition=new FormResponseDefinitionRow(Guid.NewGuid(),"T",version.Id,[version]);
        var result=FormRecordFilterPolicy.Validate(new(){Sort=sort},definition);
        Assert.True(result.IsFailure);Assert.Equal("Forms.Record.FilterInvalid",result.Error.Code);
    }
    [Theory]
    [InlineData("field")]
    [InlineData("-field")]
    public void Sorting_by_the_block_or_one_of_its_questions_is_rejected(string sort)
    {
        var label = new Dictionary<string, string> { ["es"] = "Pregunta", ["en"] = "Question" };
        var block = new CanonicalFormField(Guid.NewGuid(), "items", "group", label, null, true, null, null, Rows: new(1, 10));
        var quantity = new CanonicalFormField(Guid.NewGuid(), "quantity", "decimal", label, null, true, null, null, Repeated: true);
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{}", JsonSerializer.Serialize(new FormVersionSchema(1, [block, quantity]), JsonSerializerOptions.Web), "es", ["es", "en"]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "T", version.Id, [version]);
        foreach (var fieldId in new[] { block.FieldId, quantity.FieldId })
        {
            var result = FormRecordFilterPolicy.Validate(new() { Sort = sort, SortFieldId = fieldId }, definition);
            Assert.True(result.IsFailure);
            Assert.Equal("Forms.Record.FilterInvalid", result.Error.Code);
        }
    }
    [Theory]
    [InlineData("answered", null)]
    [InlineData("unanswered", null)]
    [InlineData("notAsked", null)]
    [InlineData("in", "true")]
    public void A_condition_on_the_block_itself_is_rejected_while_its_questions_keep_their_operators(string operation, string? value)
    {
        var label = new Dictionary<string, string> { ["es"] = "Pregunta", ["en"] = "Question" };
        var block = new CanonicalFormField(Guid.NewGuid(), "items", "group", label, null, true, null, null, Rows: new(1, 10));
        var quantity = new CanonicalFormField(Guid.NewGuid(), "quantity", "decimal", label, null, true, null, null, Repeated: true);
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{}", JsonSerializer.Serialize(new FormVersionSchema(1, [block, quantity]), JsonSerializerOptions.Web), "es", ["es", "en"]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "T", version.Id, [version]);

        var result = FormRecordFilterPolicy.Validate(new() { Conditions = [new(block.FieldId, operation, value is null ? null : [value])] }, definition);

        Assert.True(result.IsFailure);
        Assert.Equal("Forms.Record.FilterInvalid", result.Error.Code);
        Assert.True(FormRecordFilterPolicy.Validate(new() { Conditions = [new(quantity.FieldId, "gte", ["200"]), new(quantity.FieldId, "answered", null)] }, definition).IsSuccess);
    }
    // OT1-C: the fixed columns of the sheet sort without a question, and a result by the id of its column (<source>.<key>).
    [Theory]
    [InlineData("corrected", null, true)]
    [InlineData("-channel", null, true)]
    [InlineData("state", null, true)]
    [InlineData("-rules", null, true)]
    [InlineData("-result", "arca.invoice.total", true)]
    [InlineData("result", "test.number", true)]
    [InlineData("result", null, false)]
    [InlineData("result", "total", false)]
    [InlineData("result", "Arca.total", false)]
    [InlineData("result", "arca.invoice.total amount", false)]
    public async Task The_fixed_columns_and_a_result_sort_without_a_question(string sort, string? resultId, bool valid)
    {
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{}", JsonSerializer.Serialize(new FormVersionSchema(1, []), JsonSerializerOptions.Web), "es", ["es", "en"]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "T", version.Id, [version]);
        var request = new ListFormRecordsRequest { Sort = sort, SortResultId = resultId };

        var validated = await new ListFormRecordsRequestValidator().ValidateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(valid, validated.IsValid && FormRecordFilterPolicy.Validate(request, definition).IsSuccess);
    }
    // A catalog field sorts for a question of one option with a catalog, by one of the fields its column offers.
    [Theory]
    [InlineData("choice", true, true, true)]
    [InlineData("multiChoice", true, true, false)]
    [InlineData("choice", false, true, false)]
    [InlineData("choice", true, false, false)]
    public async Task A_catalog_field_sorts_only_for_a_question_of_one_option_by_a_field_of_its_catalog(string type, bool fromCatalog, bool knownField, bool valid)
    {
        var label = new Dictionary<string, string> { ["es"] = "Sucursal", ["en"] = "Branch" };
        var catalogId = Guid.NewGuid();
        var zone = new FormResponseCatalogFieldRow(Guid.NewGuid(), "zone", label, "text", "data");
        var question = new CanonicalFormField(Guid.NewGuid(), "branch", type, label, null, false, null, null, Source: fromCatalog ? new(catalogId) : null);
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{}", JsonSerializer.Serialize(new FormVersionSchema(1, [question]), JsonSerializerOptions.Web), "es", ["es", "en"]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "T", version.Id, [version],
            CatalogFields: new Dictionary<Guid, IReadOnlyList<FormResponseCatalogFieldRow>> { [catalogId] = [zone] });
        foreach (var sort in new[] { "catalogField", "-catalogField" })
        {
            var request = new ListFormRecordsRequest { Sort = sort, SortFieldId = question.FieldId, SortCatalogFieldId = knownField ? zone.Id : Guid.NewGuid() };
            var validated = await new ListFormRecordsRequestValidator().ValidateAsync(request, TestContext.Current.CancellationToken);
            Assert.True(validated.IsValid);
            Assert.Equal(valid, FormRecordFilterPolicy.Validate(request, definition).IsSuccess);
        }
        Assert.True(FormRecordFilterPolicy.Validate(new() { Sort = "catalogField", SortFieldId = question.FieldId }, definition).IsFailure);
        Assert.True(FormRecordFilterPolicy.Validate(new() { Sort = "catalogField", SortCatalogFieldId = zone.Id }, definition).IsFailure);
    }
    [Fact]
    public void Unknown_questions_versions_and_sort_fields_are_rejected()
    {
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{}", JsonSerializer.Serialize(new FormVersionSchema(1, []), JsonSerializerOptions.Web), "es", ["es", "en"]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "T", version.Id, [version]);
        Assert.True(FormRecordFilterPolicy.Validate(new() { Conditions = [new(Guid.NewGuid(), "answered", null)] }, definition).IsFailure);
        Assert.True(FormRecordFilterPolicy.Validate(new() { VersionIds = [Guid.NewGuid()] }, definition).IsFailure);
        Assert.True(FormRecordFilterPolicy.Validate(new() { Sort = "field", SortFieldId = Guid.NewGuid() }, definition).IsFailure);
    }
}
