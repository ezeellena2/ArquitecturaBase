using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogRecipientPreparationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task All_person_and_fixed_matches_are_applied_before_the_offer_limit_and_data_is_read_once_for_the_form()
    {
        var f = new FormCatalogPreparationFixture();
        var person = new FormCatalogPersonReader();
        var zone = Field("zone", "catalog", "filter", Guid.CreateVersion7());
        var category = Field("category", "text", "filter");
        Assert.True(f.Catalog.SetFields([zone, category], null).IsSuccess);
        var attribute = Attribute(zone.SourceCatalogId!.Value);
        person.Attributes.Add(attribute);
        var central = Guid.CreateVersion7().ToString("D");
        var south = Guid.CreateVersion7().ToString("D");
        person.Values[attribute.Id.ToString("D")] = central;
        for (var index = 0; index < 203; index++) Add(f, index, zone, index > 200 ? central : south, category, "cold");
        Add(f, 203, zone, central, category, "other");
        var question = Question(f) with { Source = new(f.Catalog.Id, [new(zone.Id, PersonAttributeId: attribute.Id), new(category.Id, "cold")]) };
        var second = question with { FieldId = Guid.CreateVersion7(), Key = "second" };
        var result = await new FormCatalogOptionsPolicy(f, f, person).PrepareAsync(Guid.CreateVersion7(), "es", new(1, [question, second]), "es", Ct);
        Assert.True(result.CanSend);
        Assert.Equal(2, result.Catalogs.Count);
        Assert.All(result.Catalogs, offer => Assert.Equal(f.Items.Skip(201).Take(2).Select(item => item.Id), offer.Options.Select(option => option.Id)));
        Assert.Equal(1, person.RecipientReads);
    }

    [Fact]
    public async Task A_missing_person_value_blocks_before_reading_options_and_the_assistant_cannot_prepare_that_candidate()
    {
        var f = new FormCatalogPreparationFixture();
        var people = new FormCatalogPersonReader();
        var zone = Field("zone", "catalog", "filter", Guid.CreateVersion7());
        Assert.True(f.Catalog.SetFields([zone], null).IsSuccess);
        var attribute = Attribute(zone.SourceCatalogId!.Value);
        people.Attributes.Add(attribute);
        var question = Question(f) with { Source = new(f.Catalog.Id, [new(zone.Id, PersonAttributeId: attribute.Id)]) };
        var schema = new FormVersionSchema(1, [question]);
        var policy = new FormCatalogOptionsPolicy(f, f, people);
        var prepared = await policy.PrepareAsync(Guid.CreateVersion7(), "es", schema, "es", Ct);
        Assert.False(prepared.CanSend);
        Assert.Empty(prepared.Catalogs);
        Assert.Equal(FormRecordErrors.RecipientDataMissing.Code, Assert.Single(prepared.Issues).Code);
        Assert.Equal(0, f.OfferReads);
        var candidate = new FormAssistantCandidateRow(f.DefinitionId, f.Version.Id, 1, "REC", f.Version.NameJson!, f.Version.SubmitLabelJson!,
            "es", 1, 1, "When requested", FormDraftPolicy.Serialize(schema));
        var assistant = new FormAssistantCandidatePolicy(null!, null!, null!, policy, null!);
        Assert.True((await assistant.PrepareAsync(candidate, Guid.CreateVersion7(), "es", Ct)).IsFailure);
        Assert.Equal(0, f.OfferReads);
    }

    [Theory]
    [InlineData("field")]
    [InlineData("attribute")]
    public async Task An_inactive_filter_or_attribute_stays_an_error_instead_of_dropping_the_filter(string inactive)
    {
        var f = new FormCatalogPreparationFixture();
        var people = new FormCatalogPersonReader();
        var zone = Field("zone", "catalog", "filter", Guid.CreateVersion7()) with { IsActive = inactive != "field" };
        Assert.True(f.Catalog.SetFields([zone], null).IsSuccess);
        var attribute = Attribute(zone.SourceCatalogId!.Value) with { IsActive = inactive != "attribute" };
        people.Attributes.Add(attribute);
        people.Values[attribute.Id.ToString("D")] = Guid.CreateVersion7().ToString("D");
        var question = Question(f) with { Source = new(f.Catalog.Id, [new(zone.Id, PersonAttributeId: attribute.Id)]) };
        var prepared = await new FormCatalogOptionsPolicy(f, f, people).PrepareAsync(Guid.CreateVersion7(), "es", new(1, [question]), "es", Ct);
        Assert.Equal(FormRecordErrors.CatalogFilterUnavailable.Code, Assert.Single(prepared.Issues).Code);
        Assert.Empty(prepared.Catalogs);
        Assert.Equal(0, f.OfferReads);
    }

    [Fact]
    public async Task An_inherited_presentation_and_a_question_override_are_sealed_without_changing_the_original_item()
    {
        var f = new FormCatalogPreparationFixture();
        var field = Field("tax_id", "text", "data");
        Assert.True(f.Catalog.SetFields([field], FormDraftPolicy.Serialize(new FormCatalogPresentation(["code"], [field.Id.ToString("D")]))).IsSuccess);
        var item = BusinessFormCatalogItem.Create(f.Catalog.Id, "V01", "Proveedor", "Supplier", "Dato privado", "Private data", 0);
        Assert.True(f.Catalog.SetManualItemFields(item, FormDraftPolicy.Serialize(new Dictionary<string, string> { [field.Id.ToString("D")] = "123456" })).IsSuccess);
        f.Items.Add(item);
        var inherited = Question(f);
        var own = inherited with { FieldId = Guid.CreateVersion7(), Key = "own", Source = new(f.Catalog.Id, Presentation: new(["text"], [])) };
        var prepared = await new FormCatalogOptionsPolicy(f, f, null!).PrepareAsync(Guid.CreateVersion7(), "en", new(1, [inherited, own]), "es", Ct);
        Assert.True(prepared.CanSend);
        var first = Assert.Single(prepared.Catalogs[0].Options);
        Assert.Equal("V01", first.Shown!.Title);
        Assert.Equal("123456", first.Shown.Description);
        var second = Assert.Single(prepared.Catalogs[1].Options);
        Assert.Equal("Supplier", second.Shown!.Title);
        Assert.Null(second.Shown.Description);
        Assert.Equal("Supplier", second.Text);
        Assert.Equal("Private data", second.Detail);
    }

    [Fact]
    public async Task Preview_can_inspect_long_titles_and_excess_options_while_sending_blocks_them()
    {
        var f = new FormCatalogPreparationFixture();
        Assert.True(f.Catalog.SetFields([], FormDraftPolicy.Serialize(new FormCatalogPresentation(["text", "code"], []))).IsSuccess);
        f.Items.AddRange(Enumerable.Range(0, 21).Select(index => BusinessFormCatalogItem.Create(f.Catalog.Id,
            "C" + index, new string('x', 30), null, null, null, index)));
        var question = Question(f) with { Type = "multiChoice", Selection = new(0, 20), Required = false };
        var policy = new FormCatalogOptionsPolicy(f, f, null!);
        var sending = await policy.PrepareAsync(Guid.CreateVersion7(), "es", new(1, [question]), "es", Ct);
        Assert.Equal(FormRecordErrors.CatalogTooLargeForMultiple.Code, Assert.Single(sending.Issues).Code);
        Assert.Empty(sending.Catalogs);
        var preview = await policy.PrepareAsync(Guid.CreateVersion7(), "es", new(1, [question]), "es", Ct, inspectOnly: true);
        Assert.True(preview.CanSend);
        Assert.Equal(21, Assert.Single(preview.Catalogs).Options.Count);
        Assert.All(preview.Catalogs[0].Options, option => Assert.True(option.Shown!.Title.Length > 30));
    }

    [Theory]
    [InlineData("English")]
    [InlineData("English title at thirty chars!")]
    public async Task A_common_culture_fallback_recomposes_shown_text_in_the_effective_culture(string translated)
    {
        var f = new FormCatalogPreparationFixture(originalOnly: true);
        Assert.True(f.Catalog.SetFields([], FormDraftPolicy.Serialize(new FormCatalogPresentation(["text", "code"], []))).IsSuccess);
        f.Items.Add(BusinessFormCatalogItem.Create(f.Catalog.Id, "A", "Original", translated, null, null, 0));
        var result = await f.Policy.PrepareAsync(Guid.CreateVersion7(), "en", f.DefinitionId, f.Version.Id, false, Ct);
        Assert.True(result.CanSend);
        Assert.Equal("es", result.Culture);
        Assert.Equal("Original · A", Assert.Single(Assert.Single(result.Catalogs).Options).Shown!.Title);
    }

    [Theory]
    [InlineData("English")]
    [InlineData("English title at thirty chars!")]
    public async Task Assistant_candidates_keep_the_same_effective_culture_for_the_shown_options(string translated)
    {
        var f = new FormCatalogPreparationFixture(originalOnly: true);
        Assert.True(f.Catalog.SetFields([], FormDraftPolicy.Serialize(new FormCatalogPresentation(["text", "code"], []))).IsSuccess);
        f.Items.Add(BusinessFormCatalogItem.Create(f.Catalog.Id, "A", "Original", translated, null, null, 0));
        var candidate = new FormAssistantCandidateRow(f.DefinitionId, f.Version.Id, 1, "REC", f.Version.NameJson!, f.Version.SubmitLabelJson!,
            "es", 1, 1, "When requested", f.Version.SchemaJson);
        var assistant = new FormAssistantCandidatePolicy(null!, null!, null!, new(f, f, null!), new(f.Rules, f));
        var prepared = await assistant.PrepareAsync(candidate, Guid.CreateVersion7(), "en", Ct);
        Assert.True(prepared.IsSuccess);
        Assert.Equal("es", prepared.Value.Culture);
        Assert.Equal("Original · A", Assert.Single(Assert.Single(prepared.Value.Catalogs).Options).Shown!.Title);
    }

    private static CanonicalFormField Question(FormCatalogPreparationFixture f) => Assert.Single(FormDraftPolicy.Read<FormVersionSchema>(f.Version.SchemaJson).Questions);
    private static BusinessFormCatalogField Field(string key, string type, string role, Guid? source = null) => new(Guid.CreateVersion7(), key,
        new Dictionary<string, string> { ["es"] = key }, type, source, false, false, null, null, type == "text" ? 300 : null, role, 0, true);
    private static FormPersonAttributeStoredRow Attribute(Guid source) => new(Guid.CreateVersion7(), "zone", "{\"es\":\"Zona\"}", "Catalog", source, 0, true, 1, null);
    private static void Add(FormCatalogPreparationFixture f, int index, BusinessFormCatalogField zone, string zoneValue, BusinessFormCatalogField category, string categoryValue)
    {
        var item = BusinessFormCatalogItem.Create(f.Catalog.Id, "C" + index, "Proveedor " + index, null, null, null, index);
        Assert.True(f.Catalog.SetManualItemFields(item, JsonSerializer.Serialize(new Dictionary<string, string> { [zone.Id.ToString("D")] = zoneValue,
            [category.Id.ToString("D")] = categoryValue }, JsonSerializerOptions.Web)).IsSuccess);
        f.Items.Add(item);
    }
}
