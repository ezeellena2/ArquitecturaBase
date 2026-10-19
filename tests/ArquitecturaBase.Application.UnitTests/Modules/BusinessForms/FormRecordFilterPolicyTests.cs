using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
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
