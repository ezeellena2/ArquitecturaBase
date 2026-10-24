using System.Text.Json;
using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogPublicationMatchTests : IDisposable
{
    private readonly CultureInfo previousCulture = CultureInfo.CurrentUICulture;
    public FormCatalogPublicationMatchTests() => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es");
    public void Dispose() => CultureInfo.CurrentUICulture = previousCulture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("data")]
    public async Task A_match_requires_an_active_filter_field_and_keeps_the_question_identity_and_path(string problem)
    {
        var f = new FormPublicationMultipleCatalogFixture();
        var field = Field("integer") with { Role = problem == "data" ? "data" : "filter", IsActive = problem != "inactive" };
        f.Fields = problem == "missing" ? [] : [field];
        var question = f.Field() with { Source = new(f.CatalogId, [new(field.Id, "3")]) };
        var result = await f.Guard.InspectCatalogsAsync([question], Ct);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(FormPublicationErrors.MatchInvalid.Code, issue.Code);
        Assert.Equal(question.FieldId, issue.FieldId);
        Assert.Equal("draft.fields[0].source.match", issue.Path);
        Assert.Empty(result.Warnings);
        Assert.Empty(f.MaximumReads);
    }

    [Fact]
    public async Task A_person_match_cannot_compare_options_from_different_source_catalogs()
    {
        var f = new FormPublicationMultipleCatalogFixture();
        var field = Field("catalog", Guid.CreateVersion7());
        f.Fields = [field];
        var attribute = Attribute(Guid.CreateVersion7());
        f.People.Attributes.Add(attribute);
        var question = f.Field() with { Source = new(f.CatalogId, [new(field.Id, PersonAttributeId: attribute.Id)]) };
        var result = await f.Guard.InspectCatalogsAsync([question], Ct);
        Assert.Equal(FormPublicationErrors.MatchSourceMismatch.Code, Assert.Single(result.Issues).Code);
        Assert.Empty(f.MaximumReads);
    }

    [Fact]
    public async Task Per_recipient_maximum_and_people_without_data_are_advisory_and_do_not_use_the_unfiltered_count()
    {
        var f = new FormPublicationMultipleCatalogFixture { ActiveItems = 1200 };
        var source = Guid.CreateVersion7();
        var zone = Field("catalog", source);
        var quantity = Field("integer");
        f.Fields = [zone, quantity];
        var attribute = Attribute(source);
        f.People.Attributes.Add(attribute);
        var south = Guid.CreateVersion7();
        f.Sources[source] = [new(south, source, "S", "Zona Sur", "South zone", null, null, 0, true)];
        f.Maximum = new(18, FormDraftPolicy.Serialize(new Dictionary<string, string> { [zone.Id.ToString("D")] = south.ToString("D") }));
        f.People.Missing = new(7, ["Ana", "Berta", "Clara", "Dora", "Eva", "Flora", "Gema"]);
        var question = f.Field() with { Source = new(f.CatalogId, [new(zone.Id, PersonAttributeId: attribute.Id), new(quantity.Id, "0")]) };
        var result = await f.Guard.InspectCatalogsAsync([question], Ct);
        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Warnings.Count);
        var maximum = result.Warnings.Single(warning => warning.Code == FormPublicationErrors.OptionsPerRecipientHigh.Code);
        Assert.Equal(20, maximum.Limit);
        Assert.Equal(18, maximum.Maximum);
        Assert.Equal("Zona Sur", maximum.MaximumLabel);
        var missing = result.Warnings.Single(warning => warning.Code == FormPublicationErrors.RecipientsWithoutData.Code);
        Assert.Equal(7, missing.Count);
        Assert.Equal(["Ana", "Berta", "Clara", "Dora", "Eva"], missing.People);
        Assert.All(result.Warnings, warning => Assert.Equal(question.FieldId, warning.FieldId));
        Assert.Empty(f.CountReads);
        var request = Assert.Single(f.MaximumReads);
        Assert.Equal([zone.Id], request.Fields);
        using var fixedJson = JsonDocument.Parse(request.Fixed!);
        Assert.Equal(0, fixedJson.RootElement.GetProperty(quantity.Id.ToString("D")).GetInt32());
        Assert.Equal([attribute.Id], Assert.Single(f.People.MissingReads));
        Assert.DoesNotContain("count", FormDraftPolicy.Serialize(maximum));
        Assert.DoesNotContain("maximum", FormDraftPolicy.Serialize(missing));
    }

    [Fact]
    public async Task A_fixed_filter_over_the_channel_limit_is_an_advisory_maximum_at_publication()
    {
        var f = new FormPublicationMultipleCatalogFixture { ActiveItems = 1200, Maximum = new(21, "{}") };
        var field = Field("text");
        f.Fields = [field];
        var question = f.Field() with { Source = new(f.CatalogId, [new(field.Id, "cold")]) };
        var result = await f.Guard.InspectCatalogsAsync([question], Ct);
        Assert.Empty(result.Issues);
        Assert.Equal(21, Assert.Single(result.Warnings).Maximum);
        Assert.Empty(f.CountReads);
        Assert.Empty(f.People.MissingReads);
    }

    [Fact]
    public async Task Title_problems_identify_the_inherited_catalog_or_the_question_override()
    {
        var f = new FormPublicationMultipleCatalogFixture { Presentation = new(["text", "code"], []) };
        f.Items = [new(Guid.CreateVersion7(), f.CatalogId, "LONG", new string('x', 30), null, null, null, 0, true)];
        var inherited = f.Field("choice");
        var own = f.Field("choice") with { Source = new(f.CatalogId, Presentation: new(["text", "code"], [])) };
        var validOverride = f.Field("choice") with { Source = new(f.CatalogId, Presentation: new(["code"], [])) };
        var result = await f.Guard.InspectCatalogsAsync([inherited, own, validOverride], Ct);
        Assert.Equal([FormPublicationErrors.CatalogTitleTooLong.Code, FormPublicationErrors.QuestionTitleTooLong.Code], result.Issues.Select(issue => issue.Code));
        Assert.Equal([inherited.FieldId, own.FieldId], result.Issues.Select(issue => issue.FieldId));
        Assert.All(result.Issues, issue => Assert.Equal(30, issue.Limit));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Publication_checks_every_complete_form_culture_and_keeps_item_fallback_without_requiring_translations()
    {
        var f = new FormPublicationMultipleCatalogFixture { Presentation = new(["text", "code"], []) };
        f.Items = [new(Guid.CreateVersion7(), f.CatalogId, "LONG", "Corto", new string('x', 30), null, null, 0, true)];
        var result = await f.Guard.InspectCatalogsAsync([f.Field("choice")], Ct, ["es", "en"]);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(FormPublicationErrors.CatalogTitleTooLong.Code, issue.Code);
        Assert.Equal("en", issue.Culture);
        f.Items = [f.Items[0] with { TextEn = null }];
        Assert.Empty((await f.Guard.InspectCatalogsAsync([f.Field("choice")], Ct, ["es", "en"])).Issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_description_over_three_hundred_or_an_empty_composed_title_cannot_be_published(bool emptyTitle)
    {
        var f = new FormPublicationMultipleCatalogFixture();
        var field = Field("text");
        f.Fields = [field];
        f.Presentation = emptyTitle ? new([field.Id.ToString("D")], []) : new(["text"], ["detail", "detail"]);
        f.Items = [new(Guid.CreateVersion7(), f.CatalogId, "ONE", "Option", null, new string('d', 300), null, 0, true)];
        var result = await f.Guard.InspectCatalogsAsync([f.Field("choice")], Ct);
        Assert.Equal(FormPublicationErrors.PresentationInvalid.Code, Assert.Single(result.Issues).Code);
    }

    private static BusinessFormCatalogField Field(string type, Guid? source = null) => new(Guid.CreateVersion7(), "filter",
        new Dictionary<string, string> { ["es"] = "Filtro" }, type, source, false, false, null, null,
        type == "text" ? 300 : null, "filter", 0, true);
    private static FormPersonAttributeStoredRow Attribute(Guid source) => new(Guid.CreateVersion7(), "zone", "{\"es\":\"Zona\"}", "es", "Catalog", source, 0, true, 1, null);
}
