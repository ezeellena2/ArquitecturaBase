using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogPreparationPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_foreign_versions_do_not_read_catalogs(bool foreign)
    {
        var f = new FormCatalogPreparationFixture();
        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "es", foreign ? Guid.NewGuid() : f.DefinitionId,
            foreign ? f.Version.Id : Guid.NewGuid(), false, Ct);
        Assert.Equal(FormRecordErrors.InvalidResponse.Code, Assert.Single(result.Issues).Code);
        Assert.Empty(f.Locks);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_inactive_catalogs_block_without_an_offer(bool missing)
    {
        var f = new FormCatalogPreparationFixture { CatalogMissing = missing };
        if (!missing) Assert.True(f.Catalog.SetActive(1, false).IsSuccess);
        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "es", f.DefinitionId, f.Version.Id, true, Ct);
        Assert.Equal(FormRecordErrors.CatalogUnavailable.Code, Assert.Single(result.Issues).Code);
        Assert.Empty(result.Catalogs);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_options_are_allowed_only_for_optional_questions(bool required)
    {
        var f = new FormCatalogPreparationFixture(required: required);
        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "es", f.DefinitionId, f.Version.Id, false, Ct);
        Assert.Equal(!required, result.CanSend);
        if (required) Assert.Equal(FormRecordErrors.CatalogEmpty.Code, Assert.Single(result.Issues).Code);
        else Assert.Empty(Assert.Single(result.Catalogs).Options);
    }
    [Fact]
    public async Task An_untranslated_item_keeps_its_original_text_and_detail_inside_an_English_offer()
    {
        var f = new FormCatalogPreparationFixture();
        var second = BusinessFormCatalogItem.Create(f.Catalog.Id, "TWO", "Segundo", "Second", null, null, 2);
        var first = BusinessFormCatalogItem.Create(f.Catalog.Id, "ONE", "Primero", null, "Detalle", null, 1);
        f.Items.AddRange([second, first]);
        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "en", f.DefinitionId, f.Version.Id, true, Ct);
        var offer = Assert.Single(result.Catalogs);
        Assert.Equal("en", result.Culture);
        Assert.Equal("People", offer.Label);
        Assert.Equal("People", offer.CatalogName);
        Assert.Equal(new[] { "Primero", "Second" }, offer.Options.Select(option => option.Text));
        Assert.Equal("Detalle", offer.Options[0].Detail);
        Assert.False(offer.Options[0].Translations!.ContainsKey("en"));
        Assert.Equal([f.Catalog.Id], f.Locks);
    }
    [Fact]
    public async Task Filters_use_the_recipient_and_localize_details_without_truncating_invalid_rows()
    {
        var f = new FormCatalogPreparationFixture(filtered: true);
        var recipient = Guid.NewGuid();
        f.Preview = new([new(Guid.NewGuid(), "Valid", "true"), new(Guid.NewGuid(), new string('x', 31), "true")], 2, false);
        var blocked = await f.Policy.PrepareAsync(recipient, "en", f.DefinitionId, f.Version.Id, false, Ct);
        var issue = Assert.Single(blocked.Issues);
        Assert.Equal(FormRecordErrors.CatalogOptionInvalid.Code, issue.Code);
        Assert.Equal(1, issue.InvalidOptionCount);
        Assert.Equal("People", issue.CatalogName);
        Assert.Empty(blocked.Catalogs);
        Assert.Equal(recipient, f.PreviewRecipient);
        Assert.Equal(31, f.Preview.Items[1].Text.Length);
        f.Preview = new([f.Preview.Items[0]], 1, false);
        var offered = await f.Policy.PrepareAsync(recipient, "es", f.DefinitionId, f.Version.Id, false, Ct);
        Assert.Equal("Activo", Assert.Single(Assert.Single(offered.Catalogs).Options).Detail);
    }
    [Fact]
    public async Task A_catalog_without_the_chosen_original_name_is_unpreparable_instead_of_mixing_or_throwing()
    {
        var f = new FormCatalogPreparationFixture(filtered: true);
        var names = new Dictionary<string, string> { ["en"] = "Supervisors" };
        Assert.True(f.Catalog.UpdateFilters(1, names, names, f.Catalog.FiltersJson!).IsSuccess);
        f.Preview = new([new(Guid.NewGuid(), "Ana", "true")], 1, false);
        f.SetRules([new(Guid.NewGuid(), "Approved", [], [new(Guid.NewGuid(), "NotifyAuthor", null,
            new Dictionary<string, string> { ["es"] = "Aprobamos la visita" }, false)])]);
        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "en", f.DefinitionId, f.Version.Id, false, Ct);
        Assert.False(result.CanSend);
        Assert.Empty(result.Catalogs);
        Assert.Equal(FormRecordErrors.CatalogUnavailable.Code, Assert.Single(result.Issues).Code);
    }
    [Fact]
    public async Task An_over_limit_filter_result_never_becomes_a_partial_offer()
    {
        var f = new FormCatalogPreparationFixture(filtered: true) { Preview = new([], 201, true) };
        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "es", f.DefinitionId, f.Version.Id, false, Ct);
        Assert.Equal(FormRecordErrors.CatalogTooLarge.Code, Assert.Single(result.Issues).Code);
        Assert.Empty(result.Catalogs);
    }
    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task Monolingual_catalog_names_remain_real_maps_when_the_offer_uses_another_culture(string availableCulture, string recipientCulture)
    {
        var f = new FormCatalogPreparationFixture(filtered: true);
        var names = new Dictionary<string, string> { [availableCulture] = "Supervisores" };
        Assert.True(f.Catalog.UpdateFilters(1, names, names, f.Catalog.FiltersJson!).IsSuccess);
        f.Preview = new([new(Guid.NewGuid(), "Ana", "true")], 1, false);
        var options = new FormCatalogOptionsPolicy(f, f, new FormCatalogPersonReader());
        var schema = FormDraftPolicy.Read<ArquitecturaBase.Application.Modules.BusinessForms.Models.FormVersionSchema>(f.Version.SchemaJson);
        var result = await options.PrepareAsync(Guid.NewGuid(), recipientCulture, schema, recipientCulture, Ct);
        Assert.True(result.CanSend);
        var offer = Assert.Single(result.Catalogs);
        Assert.Equal("Supervisores", offer.CatalogName);
        Assert.Equal(names, offer.CatalogNames);
        Assert.False(offer.CatalogNames!.ContainsKey(recipientCulture));
    }
}
