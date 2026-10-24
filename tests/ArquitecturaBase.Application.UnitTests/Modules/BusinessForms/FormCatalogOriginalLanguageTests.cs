using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogOriginalLanguageTests
{
    private static readonly FormCatalogFilterDefinition Filters = new("name", null, [], false, "name", false);

    [Fact]
    public async Task A_catalog_created_with_the_menu_in_English_needs_no_Spanish_texts()
    {
        using var menu = new CultureScope("en-US");
        var repository = new FormCatalogCapacityRepository();
        var work = new FakeUnitOfWork();

        var result = await Service(repository, work).CreateManualAsync(new(Only("en", "Branches"), Only("en", "Branch list"),
            [Item(null, Only("en", "North branch"))], Fields: [Field(null, Only("en", "Region"))]), Ct);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var catalog = result.Value.Catalog;
        Assert.Equal("en", catalog.OriginalCulture);
        Assert.Equal(Stored("en", "Branches"), catalog.Name);
        Assert.Equal(Stored("en", "Branch list"), catalog.Description);
        Assert.Equal(Stored("en", "Region"), Assert.Single(catalog.Fields!).Name);
        var item = Assert.Single(repository.Items);
        Assert.Null(item.TextEs);
        Assert.Equal("North branch", item.TextEn);
        Assert.Equal(1, work.Commits);
    }

    [Fact]
    public async Task A_catalog_created_with_the_menu_in_Spanish_needs_no_English_texts()
    {
        using var menu = new CultureScope("es-AR");
        var repository = new FormCatalogCapacityRepository();
        var work = new FakeUnitOfWork();

        var result = await Service(repository, work).CreateManualAsync(new(Only("es", "Sucursales"), Only("es", "Listado de sucursales"),
            [Item(null, Only("es", "Sucursal norte"))], Fields: [Field(null, Only("es", "Región"))]), Ct);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var catalog = result.Value.Catalog;
        Assert.Equal("es", catalog.OriginalCulture);
        Assert.Equal(Stored("es", "Sucursales"), catalog.Name);
        Assert.Equal(Stored("es", "Listado de sucursales"), catalog.Description);
        Assert.Equal(Stored("es", "Región"), Assert.Single(catalog.Fields!).Name);
        var item = Assert.Single(repository.Items);
        Assert.Equal("Sucursal norte", item.TextEs);
        Assert.Null(item.TextEn);
        Assert.Equal(1, work.Commits);
    }

    [Theory]
    [InlineData("es", "en-US")]
    [InlineData("en", "es-AR")]
    public async Task Saving_without_the_original_text_reports_the_original_path(string original, string menuCulture)
    {
        var other = original == "es" ? "en" : "es";
        var catalog = BusinessFormCatalog.CreateManual(original, Both("Sucursales", "Branches"), Both("Listado", "List"));
        var item = catalog.AddManualItem("NORTH", "Sucursal norte", "North branch", null, null, 1).Value;
        var field = new BusinessFormCatalogField(Guid.CreateVersion7(), "region", Both("Región", "Region"), "text", null,
            false, false, null, null, 300, "data", 0, true);
        Assert.True(catalog.SetFields([field], null).IsSuccess);
        var repository = new FormCatalogCapacityRepository(catalog, [item]);
        var work = new FakeUnitOfWork();
        var service = Service(repository, work);
        using var menu = new CultureScope(menuCulture);
        UpdateManualCatalogRequest Update(IReadOnlyDictionary<string, string?> name, IReadOnlyDictionary<string, string?> text,
            IReadOnlyDictionary<string, string?> fieldName) => new(1, name, null, [Item(item.Id, text)], Fields: [Field(field.Id, fieldName)]);
        var complete = BothRequest("Sucursal", "Branch");

        var name = await service.UpdateManualAsync(catalog.Id, Update(Only(other, "Translation"), complete, complete), Ct);
        var text = await service.UpdateManualAsync(catalog.Id, Update(complete, Only(other, "Translation"), complete), Ct);
        var fieldName = await service.UpdateManualAsync(catalog.Id, Update(complete, complete, Only(other, "Translation")), Ct);

        AssertOriginalMissing(name, "name." + original);
        AssertOriginalMissing(text, "items[0].text." + original);
        AssertOriginalMissing(fieldName, "fields[0].name." + original);
        Assert.Equal(1, catalog.Revision);
        Assert.Equal("Sucursal norte", item.TextEs);
        Assert.Equal("North branch", item.TextEn);
        Assert.Equal(0, work.Commits);
    }

    [Fact]
    public async Task A_filters_catalog_requires_only_its_original_name()
    {
        var repository = new FormCatalogCapacityRepository();
        var work = new FakeUnitOfWork();
        var service = FiltersService(repository, work);
        using (new CultureScope("en-US"))
        {
            var missing = await service.CreateAsync(new(Only("es", "Personas"), null, Filters), Ct);
            AssertOriginalMissing(missing, "name.en");
            Assert.Equal(0, work.Transactions);

            var created = await service.CreateAsync(new(Only("en", "People"), null, Filters), Ct);

            Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Code : null);
            Assert.Equal("en", created.Value.Catalog.OriginalCulture);
            Assert.Equal(Stored("en", "People"), created.Value.Catalog.Name);
        }

        using (new CultureScope("es-AR"))
        {
            var withoutOriginal = await service.UpdateAsync(repository.Catalog!.Id, new(1, Only("es", "Personas"), null, Filters), Ct);
            AssertOriginalMissing(withoutOriginal, "name.en");

            var translated = await service.UpdateAsync(repository.Catalog.Id,
                new(1, new Dictionary<string, string?> { ["en"] = "People", ["es"] = " " }, null, Filters), Ct);

            Assert.True(translated.IsSuccess, translated.IsFailure ? translated.Error.Code : null);
            Assert.Equal("en", translated.Value.Catalog.OriginalCulture);
            Assert.Equal(Stored("en", "People"), translated.Value.Catalog.Name);
        }

        Assert.Equal(2, work.Commits);
    }

    private static void AssertOriginalMissing(Result result, string path)
    {
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(FormCatalogErrors.ContentInvalid.Code, error.Code);
        Assert.Equal(path, Assert.Single(error.Errors).Key);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Only(string culture, string text) => new() { [culture] = text };

    private static Dictionary<string, string?> BothRequest(string es, string en) => new() { ["es"] = es, ["en"] = en };

    private static Dictionary<string, string> Both(string es, string en) => new() { ["es"] = es, ["en"] = en };

    private static Dictionary<string, string> Stored(string culture, string text) => new() { [culture] = text };

    private static FormCatalogManualItemSaveRequest Item(Guid? id, IReadOnlyDictionary<string, string?> text) =>
        new("north", id, "NORTH", text, null, true);

    private static FormCatalogFieldSaveRequest Field(Guid? id, IReadOnlyDictionary<string, string?> name) =>
        new(id is null ? "region" : null, id, "region", name, "text", null, false, false, null, null, 300, "data", 0, true);

    private static FormCatalogService Service(FormCatalogCapacityRepository repository, FakeUnitOfWork work)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        var reader = new FormCatalogFieldReader();
        return new(repository, reader, work,
            RequestValidators.For(new CreateManualCatalogRequestValidator(), new UpdateManualCatalogRequestValidator()),
            new(new FakeCurrentUser { UserId = actor }, permissions), null!, new(repository), new(reader));
    }

    private static FormCatalogFiltersService FiltersService(FormCatalogCapacityRepository repository, FakeUnitOfWork work)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        return new(repository, null!, work, RequestValidators.For(new CreateFiltersCatalogRequestValidator(), new UpdateFiltersCatalogRequestValidator()),
            new(new FakeCurrentUser { UserId = actor }, permissions), null!, null!);
    }
}
