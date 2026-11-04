using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogMatchPolicyTests
{
    private static readonly Guid ZoneId = Guid.Parse("01990000-0000-7000-8000-000000000001");
    private static readonly Guid ZoneFieldId = Guid.Parse("01990000-0000-7000-8000-000000000002");
    private static readonly Guid ZoneAttributeId = Guid.Parse("01990000-0000-7000-8000-000000000003");
    private static readonly Guid CentralId = Guid.Parse("01990000-0000-7000-8000-000000000004");
    private static readonly Guid CountFieldId = Guid.Parse("01990000-0000-7000-8000-000000000005");
    private static readonly Guid FlagFieldId = Guid.Parse("01990000-0000-7000-8000-000000000006");
    private static readonly Guid PriceFieldId = Guid.Parse("01990000-0000-7000-8000-000000000007");
    private static readonly Guid TextFieldId = Guid.Parse("01990000-0000-7000-8000-000000000008");
    private static readonly Guid CatalogId = Guid.Parse("01990000-0000-7000-8000-000000000009");
    private static readonly Guid FieldQuestionId = Guid.Parse("01990000-0000-7000-8000-00000000000b");

    [Fact]
    public void No_matches_preserve_the_unfiltered_offer_without_requiring_person_data()
    {
        var result = FormCatalogMatchPolicy.Containment(null, [], [], null);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Fixed_and_person_matches_build_one_typed_canonical_containment()
    {
        FormCatalogMatch[] matches = [new(PriceFieldId, "1500.50"), new(FlagFieldId, "false"),
            new(ZoneFieldId, PersonAttributeId: ZoneAttributeId), new(CountFieldId, "0"), new(TextFieldId, " Centro ")];
        var result = FormCatalogMatchPolicy.Containment(matches, Fields(), [Attribute()],
            new Dictionary<string, string> { [ZoneAttributeId.ToString("D")] = CentralId.ToString("D") });
        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value!);
        Assert.Equal(0, json.RootElement.GetProperty(CountFieldId.ToString("D")).GetInt64());
        Assert.False(json.RootElement.GetProperty(FlagFieldId.ToString("D")).GetBoolean());
        Assert.Equal("1500.5", json.RootElement.GetProperty(PriceFieldId.ToString("D")).GetString());
        Assert.Equal("Centro", json.RootElement.GetProperty(TextFieldId.ToString("D")).GetString());
        Assert.Equal(CentralId.ToString("D"), json.RootElement.GetProperty(ZoneFieldId.ToString("D")).GetString());
        Assert.Equal(5, json.RootElement.EnumerateObject().Count());
        Assert.Equal(result.Value, FormCatalogMatchPolicy.Containment(matches.Reverse().ToArray(), Fields(), [Attribute()],
            new Dictionary<string, string> { [ZoneAttributeId.ToString("D")] = CentralId.ToString("D") }).Value);
    }

    [Fact]
    public void Missing_recipient_data_never_degrades_into_the_fixed_filter_or_an_unfiltered_offer()
    {
        FormCatalogMatch[] matches = [new(CountFieldId, "3"), new(ZoneFieldId, PersonAttributeId: ZoneAttributeId)];
        var result = FormCatalogMatchPolicy.Containment(matches, Fields(), [Attribute()], new Dictionary<string, string>());
        Assert.True(result.IsFailure);
        Assert.Equal(FormRecordErrors.RecipientDataMissing.Code, result.Error.Code);
    }

    [Fact]
    public void The_missing_person_attribute_is_the_first_person_match_without_a_value_in_field_order()
    {
        var region = Guid.Parse("01990000-0000-7000-8000-00000000000a");
        FormCatalogMatch[] matches = [new(TextFieldId, PersonAttributeId: region), new(CountFieldId, "3"), new(ZoneFieldId, PersonAttributeId: ZoneAttributeId)];
        Assert.Equal(ZoneAttributeId, FormCatalogMatchPolicy.MissingPersonAttribute(matches, new Dictionary<string, string> { [region.ToString("D")] = " " }));
        Assert.Equal(region, FormCatalogMatchPolicy.MissingPersonAttribute(matches,
            new Dictionary<string, string> { [ZoneAttributeId.ToString("D")] = CentralId.ToString("D") }));
        Assert.Null(FormCatalogMatchPolicy.MissingPersonAttribute(matches, new Dictionary<string, string>
        { [ZoneAttributeId.ToString("D")] = CentralId.ToString("D"), [region.ToString("D")] = CentralId.ToString("D") }));
        Assert.Equal(ZoneAttributeId, FormCatalogMatchPolicy.MissingPersonAttribute(matches, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_or_inactive_person_attributes_stop_preparation(bool inactive)
    {
        var result = FormCatalogMatchPolicy.Containment([new(ZoneFieldId, PersonAttributeId: ZoneAttributeId)], Fields(),
            inactive ? [Attribute() with { IsActive = false }] : [],
            new Dictionary<string, string> { [ZoneAttributeId.ToString("D")] = CentralId.ToString("D") });
        Assert.True(result.IsFailure);
        Assert.Equal(FormRecordErrors.CatalogFilterUnavailable.Code, result.Error.Code);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("data")]
    public void A_field_that_cannot_filter_is_rejected_before_it_can_be_removed_from_the_containment(string problem)
    {
        var fields = problem == "missing" ? [] : Fields().Select(field => field.Id == ZoneFieldId
            ? field with { IsActive = problem != "inactive", Role = problem == "data" ? "data" : "filter" } : field).ToArray();
        var error = FormCatalogMatchPolicy.Validate([new(ZoneFieldId, PersonAttributeId: ZoneAttributeId)], fields, [Attribute()]);
        Assert.Equal(FormPublicationErrors.MatchInvalid.Code, error!.Code);
        var prepared = FormCatalogMatchPolicy.Containment([new(ZoneFieldId, PersonAttributeId: ZoneAttributeId)], fields, [Attribute()],
            new Dictionary<string, string> { [ZoneAttributeId.ToString("D")] = CentralId.ToString("D") });
        Assert.Equal(FormRecordErrors.CatalogFilterUnavailable.Code, prepared.Error.Code);
    }

    [Theory]
    [InlineData("other-source")]
    [InlineData("text-attribute")]
    [InlineData("text-field")]
    public void Person_matches_require_catalog_types_and_the_same_source_option_identity(string problem)
    {
        var fields = Fields().Select(field => field.Id == ZoneFieldId && problem == "text-field"
            ? field with { Type = "text", SourceCatalogId = null } : field).ToArray();
        var attribute = Attribute() with { Type = problem == "text-attribute" ? "Text" : "Catalog",
            SourceCatalogId = problem == "other-source" ? CatalogId : ZoneId };
        var error = FormCatalogMatchPolicy.Validate([new(ZoneFieldId, PersonAttributeId: ZoneAttributeId)], fields, [attribute]);
        Assert.Equal(FormPublicationErrors.MatchSourceMismatch.Code, error!.Code);
    }

    [Theory]
    [InlineData("integer", "3 days")]
    [InlineData("boolean", "yes")]
    [InlineData("date", "2027-02-30")]
    [InlineData("catalog", "Central")]
    public void Invalid_fixed_values_cannot_turn_into_unfiltered_offers(string type, string value)
    {
        var field = Fields()[0] with { Type = type, SourceCatalogId = type == "catalog" ? ZoneId : null };
        var error = FormCatalogMatchPolicy.Validate([new(field.Id, value)], [field], []);
        Assert.Equal(FormPublicationErrors.MatchInvalid.Code, error!.Code);
    }

    [Fact]
    public void A_match_by_another_answer_filters_by_the_option_chosen_in_that_question()
    {
        FormCatalogMatch[] matches = [new(CountFieldId, "3"), new(ZoneFieldId, QuestionId: FieldQuestionId)];
        var result = FormCatalogMatchPolicy.Containment(matches, Fields(), [], null, new Dictionary<Guid, Guid> { [FieldQuestionId] = CentralId });
        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value!);
        Assert.Equal(CentralId.ToString("D"), json.RootElement.GetProperty(ZoneFieldId.ToString("D")).GetString());
        Assert.Equal(3, json.RootElement.GetProperty(CountFieldId.ToString("D")).GetInt32());
    }

    [Fact]
    public void Without_the_answer_it_depends_on_the_offer_is_never_built()
    {
        FormCatalogMatch[] matches = [new(CountFieldId, "3"), new(ZoneFieldId, QuestionId: FieldQuestionId)];
        Assert.Equal(FormRecordErrors.CatalogDependsOnAnswer.Code, FormCatalogMatchPolicy.Containment(matches, Fields(), [], null).Error.Code);
        Assert.Equal(FormRecordErrors.CatalogDependsOnAnswer.Code, FormCatalogMatchPolicy.Containment(matches, Fields(), [], null,
            new Dictionary<Guid, Guid> { [CentralId] = CentralId }).Error.Code);
    }

    [Fact]
    public void A_missing_person_datum_is_told_before_the_answer_the_offer_waits_for()
    {
        var lot = Fields()[0] with { Id = TextFieldId, Key = "lot" };
        FormCatalogMatch[] matches = [new(ZoneFieldId, PersonAttributeId: ZoneAttributeId), new(TextFieldId, QuestionId: FieldQuestionId)];
        var result = FormCatalogMatchPolicy.Containment(matches, [Fields()[0], lot], [Attribute()], new Dictionary<string, string>());
        Assert.Equal(FormRecordErrors.RecipientDataMissing.Code, result.Error.Code);
    }

    [Theory]
    [InlineData("value")]
    [InlineData("person")]
    [InlineData("empty")]
    public void A_match_names_exactly_one_fixed_value_person_datum_or_question(string problem)
    {
        FormCatalogMatch match = problem switch
        {
            "value" => new(ZoneFieldId, CentralId.ToString("D"), QuestionId: FieldQuestionId),
            "person" => new(ZoneFieldId, PersonAttributeId: ZoneAttributeId, QuestionId: FieldQuestionId),
            _ => new(ZoneFieldId, QuestionId: Guid.Empty),
        };
        Assert.True(FormCatalogMatchPolicy.HasValidShape([new(ZoneFieldId, QuestionId: FieldQuestionId)]));
        Assert.False(FormCatalogMatchPolicy.HasValidShape([match]));
    }

    [Theory]
    [InlineData("inactive", "Forms.Publication.MatchInvalid")]
    [InlineData("data", "Forms.Publication.MatchInvalid")]
    [InlineData("text", "Forms.Publication.MatchQuestionInvalid")]
    public void A_match_by_another_answer_needs_an_active_catalog_filter_field(string problem, string code)
    {
        var fields = Fields().Select(field => field.Id != ZoneFieldId ? field : problem switch
        {
            "inactive" => field with { IsActive = false },
            "data" => field with { Role = "data" },
            _ => field with { Type = "text", SourceCatalogId = null, MaxLength = 300 },
        }).ToArray();
        Assert.Null(FormCatalogMatchPolicy.Validate([new(ZoneFieldId, QuestionId: FieldQuestionId)], Fields(), []));
        Assert.Equal(code, FormCatalogMatchPolicy.Validate([new(ZoneFieldId, QuestionId: FieldQuestionId)], fields, [])!.Code);
    }

    [Fact]
    public void Only_an_earlier_single_choice_question_of_the_field_source_can_give_the_answer()
    {
        FormCatalogMatch[] matches = [new(CountFieldId, "3"), new(ZoneFieldId, QuestionId: FieldQuestionId)];
        Assert.Null(FormCatalogMatchPolicy.ValidateQuestions(matches, Fields(), [Question(FieldQuestionId, "choice", ZoneId)]));
        CanonicalFormField[][] wrong = [[], [Question(FieldQuestionId, "multiChoice", ZoneId)], [Question(FieldQuestionId, "choice", CatalogId)],
            [Question(FieldQuestionId, "shortText", null)], [Question(CentralId, "choice", ZoneId)]];
        Assert.All(wrong, earlier => Assert.Equal(FormPublicationErrors.MatchQuestionInvalid.Code,
            FormCatalogMatchPolicy.ValidateQuestions(matches, Fields(), earlier)!.Code));
    }

    [Fact]
    public void The_fixed_subset_keeps_its_json_types_without_requiring_a_recipient()
    {
        var result = FormCatalogMatchPolicy.FixedContainment([new(ZoneFieldId, PersonAttributeId: ZoneAttributeId), new(CountFieldId, "3"),
            new(FlagFieldId, QuestionId: FieldQuestionId)], Fields());
        Assert.True(result.IsSuccess);
        using var json = JsonDocument.Parse(result.Value!);
        var value = Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal(CountFieldId.ToString("D"), value.Name);
        Assert.Equal(3, value.Value.GetInt32());
    }

    private static BusinessFormCatalogField[] Fields() => [
        new(ZoneFieldId, "zone", Names(), "catalog", ZoneId, false, false, null, null, null, "filter", 0, true),
        new(CountFieldId, "count", Names(), "integer", null, false, false, null, null, null, "filter", 1, true),
        new(FlagFieldId, "flag", Names(), "boolean", null, false, false, null, null, null, "filter", 2, true),
        new(PriceFieldId, "price", Names(), "decimal", null, false, false, null, null, null, "filter", 3, true),
        new(TextFieldId, "text", Names(), "text", null, false, false, null, null, 300, "filter", 4, true)];
    private static FormPersonAttributeStoredRow Attribute() => new(ZoneAttributeId, "zone", "{\"es\":\"Zona\"}", "es", "Catalog", ZoneId, 0, true, 1, null);
    private static CanonicalFormField Question(Guid id, string type, Guid? catalog) => new(id, "field_" + id.ToString("N"), type,
        Names(), null, true, null, null, catalog is null ? null : new FormQuestionSource(catalog), Selection: type == "multiChoice" ? new(0, 20) : null);
    private static Dictionary<string, string> Names() => new() { ["es"] = "Zona", ["en"] = "Zone" };
}
