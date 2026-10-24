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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Spanish_item_with_no_Spanish_detail_shows_its_English_original_detail_without_forging_its_translation(bool forSending)
    {
        // The catalog was written in English: the Spanish translation has a name but no detail, so only English can supply it.
        var fixture = new FormCatalogPreparationFixture(catalogCulture: "en");
        fixture.Items.Add(BusinessFormCatalogItem.Create(fixture.Catalog.Id, "ONE", "Nombre", "Name", null, "Original detail", 0));

        var prepared = await fixture.Policy.PrepareAsync(Guid.NewGuid(), "es", fixture.DefinitionId, fixture.Version.Id, forSending, Ct);

        Assert.True(prepared.CanSend);
        Assert.Equal("es", prepared.Culture);
        var option = Assert.Single(Assert.Single(prepared.Catalogs).Options);
        Assert.Equal("Nombre", option.Text);
        Assert.Equal("Original detail", option.Detail);
        Assert.Null(option.Translations!["es"].Detail);
        Assert.Equal("Original detail", option.Translations["en"].Detail);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Spanish_form_keeps_its_culture_with_a_catalog_named_only_in_English(bool forSending)
    {
        var fixture = new FormCatalogPreparationFixture(catalogCulture: "en");
        var names = new Dictionary<string, string> { ["en"] = "Branches" };
        Assert.True(fixture.Catalog.UpdateManual(1, names, new Dictionary<string, string>()).IsSuccess);
        fixture.Items.Add(BusinessFormCatalogItem.Create(fixture.Catalog.Id, "NORTH", "Sucursal norte", "North branch", null, null, 0));

        var prepared = await fixture.Policy.PrepareAsync(Guid.NewGuid(), "es", fixture.DefinitionId, fixture.Version.Id, forSending, Ct);

        Assert.True(prepared.CanSend);
        Assert.Equal("es", prepared.Culture);
        var offer = Assert.Single(prepared.Catalogs);
        Assert.Equal("Personas", offer.Label);
        Assert.Equal("Branches", offer.CatalogName);
        Assert.Equal("Sucursal norte", Assert.Single(offer.Options).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Spanish_form_offers_an_English_only_item_with_its_original_text(bool forSending)
    {
        var fixture = new FormCatalogPreparationFixture(catalogCulture: "en");
        fixture.Items.Add(BusinessFormCatalogItem.Create(fixture.Catalog.Id, "NORTH", null, "North branch", null, "Main office", 0));

        var prepared = await fixture.Policy.PrepareAsync(Guid.NewGuid(), "es", fixture.DefinitionId, fixture.Version.Id, forSending, Ct);

        Assert.True(prepared.CanSend);
        Assert.Equal("es", prepared.Culture);
        var option = Assert.Single(Assert.Single(prepared.Catalogs).Options);
        Assert.Equal("North branch", option.Text);
        Assert.Equal("Main office", option.Detail);
        Assert.Equal(["en"], option.Translations!.Keys);
        Assert.Equal("North branch", option.Translations["en"].Text);
    }

    // The catalog name falls back to its own original like its options, so a complete secondary form keeps its culture.
    [Fact]
    public async Task A_missing_secondary_catalog_name_shows_the_catalog_original_without_changing_the_form_culture()
    {
        var fixture = new FormCatalogPreparationFixture(filtered: true);
        var names = new Dictionary<string, string> { ["es"] = "Personas originales" };
        Assert.True(fixture.Catalog.UpdateFilters(1, names, names, fixture.Catalog.FiltersJson!).IsSuccess);
        fixture.Preview = new([new(Guid.NewGuid(), "Ana", "true")], 1, false);

        var prepared = await fixture.Policy.PrepareAsync(Guid.NewGuid(), "en", fixture.DefinitionId, fixture.Version.Id, false, Ct);

        Assert.True(prepared.CanSend);
        Assert.Equal("en", prepared.Culture);
        var offer = Assert.Single(prepared.Catalogs);
        Assert.Equal("People", offer.Label);
        Assert.Equal("Personas originales", offer.CatalogName);
        Assert.False(offer.CatalogNames!.ContainsKey("en"));
    }
}
