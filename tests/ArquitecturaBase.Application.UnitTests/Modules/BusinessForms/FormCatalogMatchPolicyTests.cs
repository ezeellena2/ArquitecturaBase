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
    public void The_fixed_subset_keeps_its_json_types_without_requiring_a_recipient()
    {
        var result = FormCatalogMatchPolicy.FixedContainment([new(ZoneFieldId, PersonAttributeId: ZoneAttributeId), new(CountFieldId, "3")], Fields());
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
    private static Dictionary<string, string> Names() => new() { ["es"] = "Zona", ["en"] = "Zone" };
}
