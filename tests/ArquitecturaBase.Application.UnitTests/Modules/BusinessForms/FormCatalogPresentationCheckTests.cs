using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// The question's own presentation is checked on the server over every active option of its source, in each language
// the catalog has, and not only over the page the editor has loaded.
public sealed class FormCatalogPresentationCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Checking_a_question_presentation_counts_every_active_option_in_each_catalog_language()
    {
        var catalog = Catalog();
        var reader = new FormCatalogFieldReader();
        reader.Catalogs[catalog.Id] = Row(catalog);
        // Listed last to first, so the examples must come from the order by Position and not from the list.
        var active = Enumerable.Range(0, 25).Reverse().Select(position => new FormCatalogStoredItemRow(Guid.CreateVersion7(), catalog.Id,
            $"CODE{position:000000}", $"Spanish text number {position:00000}", "Short", null, null, position, true)).ToArray();
        var inactive = new FormCatalogStoredItemRow(Guid.CreateVersion7(), catalog.Id, "INACTIVE00", new string('S', 40), new string('E', 40),
            null, null, 25, false);
        reader.Items[catalog.Id] = [.. active, inactive];

        var result = await Service(catalog, reader).CheckPresentationAsync(catalog.Id,
            new(new FormCatalogPresentation(["code", "text"], [])), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["es", "en"], result.Value.Cultures.Select(check => check.Culture));
        var spanish = result.Value.Cultures[0];
        Assert.Equal(25, spanish.TitleTooLongCount);
        var first = active.OrderBy(item => item.Position).Take(3).ToArray();
        Assert.Equal(first.Select(item => new FormCatalogPresentationExample(item.Id, item.Code + " · " + item.TextEs)), spanish.TitleExamples);
        Assert.Equal(0, spanish.DescriptionTooLongCount);
        var english = result.Value.Cultures[1];
        Assert.Equal(0, english.TitleTooLongCount);
        Assert.Empty(english.TitleExamples);
    }

    [Fact]
    public async Task Checking_a_presentation_with_a_field_that_is_not_active_is_invalid()
    {
        var catalog = Catalog();
        var field = new BusinessFormCatalogField(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Field" }, "text", null,
            false, false, null, null, 300, "data", 0, false);
        Assert.True(catalog.SetFields([field], null).IsSuccess);
        var reader = new FormCatalogFieldReader();
        reader.Catalogs[catalog.Id] = Row(catalog);

        var result = await Service(catalog, reader).CheckPresentationAsync(catalog.Id,
            new(new FormCatalogPresentation(["text", field.Id.ToString("D")], [])), Ct);

        Assert.Equal("Forms.Catalog.PresentationInvalid", result.Error.Code);
        Assert.Equal("presentation", Assert.Single(Assert.IsType<ValidationError>(result.Error).Errors.Keys));
    }

    [Fact]
    public async Task Checking_a_presentation_of_a_filters_catalog_is_invalid()
    {
        var catalog = Catalog();
        var reader = new FormCatalogFieldReader();
        reader.Catalogs[catalog.Id] = Row(catalog) with { Kind = "Filters" };

        var result = await Service(catalog, reader).CheckPresentationAsync(catalog.Id,
            new(new FormCatalogPresentation(["text"], [])), Ct);

        Assert.Equal("Forms.Catalog.PresentationInvalid", result.Error.Code);
        Assert.Equal("presentation", Assert.Single(Assert.IsType<ValidationError>(result.Error).Errors.Keys));
    }

    [Fact]
    public async Task Checking_without_a_presentation_is_rejected_before_reading_the_catalog()
    {
        var catalog = Catalog();

        var result = await Service(catalog, new FormCatalogFieldReader()).CheckPresentationAsync(catalog.Id, new(null), Ct);

        Assert.Contains("presentation", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
    }

    [Fact]
    public async Task Checking_the_presentation_of_an_unknown_catalog_is_not_found()
    {
        var catalog = Catalog();

        var result = await Service(catalog, new FormCatalogFieldReader()).CheckPresentationAsync(catalog.Id,
            new(new FormCatalogPresentation(["text"], [])), Ct);

        Assert.Equal(FormCatalogErrors.NotFound.Code, result.Error.Code);
    }

    private static FormCatalogService Service(BusinessFormCatalog catalog, FormCatalogFieldReader reader)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead];
        var repository = new FormCatalogActivationRepository(catalog);
        return new(repository, reader, new FakeUnitOfWork(), RequestValidators.For(new FormCatalogPresentationCheckRequestValidator()),
            new FormCatalogAccessGuard(new FakeCurrentUser { UserId = actor }, permissions), null!, new(repository), new(reader));
    }

    private static FormCatalogStoredRow Row(BusinessFormCatalog catalog) => new(catalog.Id, catalog.Key, catalog.NameJson, catalog.DescriptionJson,
        catalog.OriginalCulture, "Manual", 1, true, catalog.CreatedAtUtc, null, null, null, 1, FieldsJson: catalog.FieldsJson,
        PresentationJson: catalog.PresentationJson);

    private static BusinessFormCatalog Catalog() => BusinessFormCatalog.CreateManual("es",
        new Dictionary<string, string> { ["es"] = "Catálogo", ["en"] = "Catalog" },
        new Dictionary<string, string> { ["es"] = "Descripción", ["en"] = "Description" });
}
