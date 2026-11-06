using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// VO1-A (2026-11-06): the official preview of a published version is gone through like on the phone, so its catalog lists carry
// sample options: the first ones of each list, in order, without person filters or filters by another answer (the preview is for
// nobody in particular); fixed filters still apply.
public sealed class FormCatalogPreviewOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_preview_offers_the_first_twenty_active_options_of_each_list_in_order()
    {
        var f = new FormCatalogPreparationFixture();
        for (var index = 0; index < 25; index++) f.Items.Add(BusinessFormCatalogItem.Create(f.Catalog.Id, "C" + index, "Artículo " + index, null, null, null, 25 - index));
        var inactive = f.Items.Single(item => item.Code == "C24");
        Assert.True(f.Catalog.SetManualItemActive(inactive, false).IsSuccess);
        var offers = await new FormCatalogOptionsPolicy(f, f, new FormCatalogPersonReader()).PreviewAsync(Guid.CreateVersion7(), "es", Schema(f), "es", Ct);
        var offer = Assert.Single(offers);
        Assert.Equal(f.FieldId, offer.FieldId);
        Assert.Equal(Question(f).Key, offer.Key);
        Assert.Equal(FormCatalogOptionsPolicy.PreviewOptionLimit, offer.Options.Count);
        Assert.Equal(f.Items.Where(item => item.IsActive).OrderBy(item => item.Position).Take(20).Select(item => item.Id), offer.Options.Select(option => option.Id));
    }

    [Fact]
    public async Task A_preview_ignores_person_filters_but_keeps_the_fixed_ones()
    {
        var f = new FormCatalogPreparationFixture();
        var people = new FormCatalogPersonReader();
        var zone = Field("zone", "catalog", Guid.CreateVersion7());
        var category = Field("category", "text");
        Assert.True(f.Catalog.SetFields([zone, category], null).IsSuccess);
        var attribute = new FormPersonAttributeStoredRow(Guid.CreateVersion7(), "zone", "{\"es\":\"Zona\"}", "es", "Catalog", zone.SourceCatalogId!.Value, 0, true, 1, null);
        people.Attributes.Add(attribute);
        for (var index = 0; index < 4; index++) Add(f, index, zone, Guid.CreateVersion7().ToString("D"), category, index % 2 == 0 ? "cold" : "other");
        var question = Question(f) with { Source = new(f.Catalog.Id, [new(zone.Id, PersonAttributeId: attribute.Id), new(category.Id, "cold")]) };
        // Nobody's data is read: the viewer has none and the list does not depend on it.
        var offers = await new FormCatalogOptionsPolicy(f, f, people).PreviewAsync(Guid.CreateVersion7(), "es", new(1, [question]), "es", Ct);
        Assert.Equal(f.Items.Where((_, index) => index % 2 == 0).Select(item => item.Id), Assert.Single(offers).Options.Select(option => option.Id));
        Assert.Equal(0, people.RecipientReads);
    }

    [Fact]
    public async Task A_preview_offers_a_list_that_depends_on_another_answer_without_that_filter()
    {
        var f = new FormCatalogPreparationFixture(dependsOnAnswer: true);
        f.Items.Add(BusinessFormCatalogItem.Create(f.Catalog.Id, "OMBU", "El Ombú", null, null, null, 1));
        var offers = await new FormCatalogOptionsPolicy(f, f, new FormCatalogPersonReader()).PreviewAsync(Guid.CreateVersion7(), "es", Schema(f), "es", Ct);
        Assert.Equal(2, offers.Count);
        Assert.All(offers, offer => Assert.Equal("El Ombú", Assert.Single(offer.Options).Text));
    }

    [Fact]
    public async Task A_people_list_is_previewed_as_the_viewer_would_get_it()
    {
        var f = new FormCatalogPreparationFixture(filtered: true) { Preview = new([new(Guid.CreateVersion7(), "Ana", null), new(Guid.CreateVersion7(), "Luis", null)], 2, false) };
        var viewer = Guid.CreateVersion7();
        var offers = await new FormCatalogOptionsPolicy(f, f, new FormCatalogPersonReader()).PreviewAsync(viewer, "es", Schema(f), "es", Ct);
        Assert.Equal(["Ana", "Luis"], Assert.Single(offers).Options.Select(option => option.Text));
        Assert.Equal(viewer, f.PreviewRecipient);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_preview_leaves_out_a_catalog_that_cannot_be_offered(bool missing)
    {
        var f = new FormCatalogPreparationFixture { CatalogMissing = missing };
        if (!missing) Assert.True(f.Catalog.SetActive(1, false).IsSuccess);
        var offers = await new FormCatalogOptionsPolicy(f, f, new FormCatalogPersonReader()).PreviewAsync(Guid.CreateVersion7(), "es", Schema(f), "es", Ct);
        Assert.Empty(offers);
    }

    [Fact]
    public async Task A_preview_reads_the_sealed_version_of_its_own_form()
    {
        var f = new FormCatalogPreparationFixture();
        f.Items.Add(BusinessFormCatalogItem.Create(f.Catalog.Id, "OMBU", "El Ombú", null, null, null, 1));
        var offer = Assert.Single(await f.Policy.PreviewAsync(Guid.CreateVersion7(), "es", f.DefinitionId, f.Version.Id, Ct));
        Assert.Equal("El Ombú", Assert.Single(offer.Options).Text);
        Assert.Empty(await f.Policy.PreviewAsync(Guid.CreateVersion7(), "es", Guid.NewGuid(), f.Version.Id, Ct));
        Assert.Empty(await f.Policy.PreviewAsync(Guid.CreateVersion7(), "es", f.DefinitionId, Guid.NewGuid(), Ct));
    }

    private static FormVersionSchema Schema(FormCatalogPreparationFixture f) => FormDraftPolicy.Read<FormVersionSchema>(f.Version.SchemaJson);
    private static CanonicalFormField Question(FormCatalogPreparationFixture f) => Schema(f).Questions.Single(question => question.FieldId == f.FieldId);
    private static BusinessFormCatalogField Field(string key, string type, Guid? source = null) => new(Guid.CreateVersion7(), key,
        new Dictionary<string, string> { ["es"] = key }, type, source, false, false, null, null, type == "text" ? 300 : null, "filter", 0, true);
    private static void Add(FormCatalogPreparationFixture f, int index, BusinessFormCatalogField zone, string zoneValue, BusinessFormCatalogField category, string categoryValue)
    {
        var item = BusinessFormCatalogItem.Create(f.Catalog.Id, "C" + index, "Proveedor " + index, null, null, null, index);
        Assert.True(f.Catalog.SetManualItemFields(item, JsonSerializer.Serialize(new Dictionary<string, string> { [zone.Id.ToString("D")] = zoneValue,
            [category.Id.ToString("D")] = categoryValue }, JsonSerializerOptions.Web)).IsSuccess);
        f.Items.Add(item);
    }
}
