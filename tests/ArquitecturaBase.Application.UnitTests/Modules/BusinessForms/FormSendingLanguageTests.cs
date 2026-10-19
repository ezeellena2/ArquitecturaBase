using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormSendingLanguageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("es", "en", "all")]
    [InlineData("en", "es", "all")]
    [InlineData("es", "en", "name")]
    [InlineData("en", "es", "name")]
    [InlineData("es", "en", "submitLabel")]
    [InlineData("en", "es", "submitLabel")]
    [InlineData("es", "en", "label")]
    [InlineData("en", "es", "label")]
    [InlineData("es", "en", "help")]
    [InlineData("en", "es", "help")]
    [InlineData("es", "en", "option")]
    [InlineData("en", "es", "option")]
    public async Task Missing_secondary_form_texts_choose_the_original_without_catalogs_or_invented_translations(
        string original, string secondary, string missingPart)
    {
        IReadOnlyDictionary<string, string> Text(string part) => missingPart == "all" || missingPart == part
            ? new Dictionary<string, string> { [original] = "Original" }
            : new Dictionary<string, string> { [original] = "Original", [secondary] = "Translation" };
        var fieldId = Guid.NewGuid();
        var schema = new FormVersionSchema(1, [new(fieldId, "field_" + fieldId.ToString("N"), "choice",
            Text("label"), Text("help"), true, null, [new(Guid.NewGuid(), Text("option"))])]);
        var name = FormDraftPolicy.Serialize(Text("name"));
        var submit = FormDraftPolicy.Serialize(Text("submitLabel"));
        var unchangedSchema = FormDraftPolicy.Serialize(schema);
        var fixture = new FormCatalogPreparationFixture(originalCulture: original);
        var policy = new FormPresentationCulturePolicy(fixture.Rules, fixture);

        var prepared = await policy.PrepareAsync(fixture.DefinitionId, original, secondary, name, submit,
            schema, new StructuredFormPreparationResponse([], []) { Culture = secondary }, Ct);

        Assert.True(prepared.CanSend);
        Assert.Equal(original, prepared.Culture);
        Assert.Empty(prepared.Catalogs);
        Assert.Empty(fixture.Locks);
        Assert.Equal(unchangedSchema, FormDraftPolicy.Serialize(schema));
    }

    [Theory]
    [InlineData("es", "en", false)]
    [InlineData("es", "en", true)]
    [InlineData("en", "es", false)]
    [InlineData("en", "es", true)]
    public async Task Original_only_catalog_questions_are_prepared_for_an_account_in_the_other_language(
        string original, string secondary, bool forSending)
    {
        var fixture = new FormCatalogPreparationFixture(originalCulture: original, originalOnly: true);
        fixture.Items.Add(BusinessFormCatalogItem.Create(fixture.Catalog.Id, "ONE", "Nombre original", "Original name",
            "Detalle original", "Original detail", 0));

        var prepared = await fixture.Policy.PrepareAsync(Guid.NewGuid(), secondary, fixture.DefinitionId,
            fixture.Version.Id, forSending, Ct);

        Assert.True(prepared.CanSend);
        Assert.Equal(original, prepared.Culture);
        var offer = Assert.Single(prepared.Catalogs);
        Assert.Equal(original == "es" ? "Personas" : "People", offer.Label);
        Assert.Equal(original == "es" ? "Personas" : "People", offer.CatalogName);
        Assert.Equal(original == "es" ? "Nombre original" : "Original name", Assert.Single(offer.Options).Text);
        Assert.Equal(forSending ? new[] { fixture.Catalog.Id } : Array.Empty<Guid>(), fixture.Locks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_English_item_with_no_English_detail_keeps_the_original_detail_without_forging_its_translation(bool forSending)
    {
        var fixture = new FormCatalogPreparationFixture();
        fixture.Items.Add(BusinessFormCatalogItem.Create(fixture.Catalog.Id, "ONE", "Nombre", "Name", "Detalle original", null, 0));

        var prepared = await fixture.Policy.PrepareAsync(Guid.NewGuid(), "en", fixture.DefinitionId, fixture.Version.Id, forSending, Ct);

        Assert.True(prepared.CanSend);
        Assert.Equal("en", prepared.Culture);
        var option = Assert.Single(Assert.Single(prepared.Catalogs).Options);
        Assert.Equal("Name", option.Text);
        Assert.Equal("Detalle original", option.Detail);
        Assert.Null(option.Translations!["en"].Detail);
        Assert.Equal("Detalle original", option.Translations["es"].Detail);
    }

    [Fact]
    public async Task A_missing_secondary_catalog_name_still_falls_back_the_complete_offer_to_the_original()
    {
        var fixture = new FormCatalogPreparationFixture(filtered: true);
        var names = new Dictionary<string, string> { ["es"] = "Personas originales" };
        Assert.True(fixture.Catalog.UpdateFilters(1, names, names, fixture.Catalog.FiltersJson!).IsSuccess);
        fixture.Preview = new([new(Guid.NewGuid(), "Ana", "true")], 1, false);

        var prepared = await fixture.Policy.PrepareAsync(Guid.NewGuid(), "en", fixture.DefinitionId, fixture.Version.Id, false, Ct);

        Assert.True(prepared.CanSend);
        Assert.Equal("es", prepared.Culture);
        var offer = Assert.Single(prepared.Catalogs);
        Assert.Equal("Personas", offer.Label);
        Assert.Equal("Personas originales", offer.CatalogName);
        Assert.False(offer.CatalogNames!.ContainsKey("en"));
    }
}
