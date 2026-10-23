using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogTypedServiceTests
{
    [Theory]
    [InlineData("Integer", "1500")]
    [InlineData("Decimal", "1500.00")]
    [InlineData("Date", "2026-10-21")]
    public async Task Creating_a_typed_catalog_stores_its_type_and_canonical_item_value(string type, string value)
    {
        var repository = new FormCatalogCapacityRepository();
        var work = new FakeUnitOfWork();
        var result = await Service(repository, work).CreateManualAsync(new(Texts(), Texts(),
            [Input(value)], type), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(type, result.Value.Catalog.ValueType);
        Assert.Equal(type, repository.Catalog!.ValueType.ToString());
        Assert.Equal(type == "Decimal" ? "1500" : value, Assert.Single(repository.Items).Value);
        Assert.Equal(1, work.Commits);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_typed_item_requires_a_value_even_when_inactive(bool active)
    {
        var repository = new FormCatalogCapacityRepository();
        var work = new FakeUnitOfWork();
        var result = await Service(repository, work).CreateManualAsync(new(Texts(), Texts(),
            [Input(null) with { IsActive = active }], "Decimal"), Ct);
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal("Forms.Catalog.ItemValueRequired", error.Code);
        Assert.Equal("items[0].value", Assert.Single(error.Errors).Key);
        Assert.Empty(repository.Items);
        Assert.Equal(0, work.Commits);
    }

    [Theory]
    [InlineData(null, "Forms.Catalog.ItemValueRequired")]
    [InlineData("not-a-number", "Forms.Catalog.ItemValueInvalid")]
    [InlineData("0.00000000000000000000000000001", "Forms.Catalog.ItemValueInvalid")]
    public async Task Editing_reports_the_value_error_at_its_row(string? value, string expectedCode)
    {
        var metadata = new Dictionary<string, string> { ["es"] = "Zonas", ["en"] = "Areas" };
        var catalog = BusinessFormCatalog.CreateManual("es", metadata, metadata, BusinessFormCatalogValueType.Decimal);
        var item = catalog.AddManualItem("GBA", "GBA", null, null, null, 1, "1500").Value;
        var repository = new FormCatalogCapacityRepository(catalog, [item]);
        var work = new FakeUnitOfWork();
        var result = await Service(repository, work).UpdateManualAsync(catalog.Id, new(1, Texts(), Texts(),
            [Input(value) with { ItemId = item.Id }]), Ct);
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal("items[0].value", Assert.Single(error.Errors).Key);
        Assert.Equal("1500", item.Value);
        Assert.Equal(0, work.Commits);
    }

    [Theory]
    [InlineData("decimal")]
    [InlineData("Boolean")]
    [InlineData("999")]
    [InlineData("")]
    public async Task Unknown_value_types_are_rejected_before_the_transaction(string type)
    {
        var work = new FakeUnitOfWork();
        var repository = new FormCatalogCapacityRepository();
        var result = await Service(repository, work).CreateManualAsync(new(Texts(), Texts(), [], type), Ct);
        Assert.True(result.IsFailure);
        Assert.Contains("valueType", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        Assert.Equal(0, work.Transactions);
        Assert.Empty(repository.Locks);
    }

    [Theory]
    [InlineData("es-AR", "es")]
    [InlineData("en-US", "en")]
    public async Task A_created_catalog_takes_its_original_language_from_the_menu(string uiCulture, string original)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiCulture);
        try
        {
            var repository = new FormCatalogCapacityRepository();

            var result = await Service(repository, new FakeUnitOfWork()).CreateManualAsync(new(Texts(), Texts(), [], null), Ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(original, repository.Catalog!.OriginalCulture);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("es-AR", "es")]
    [InlineData("en-US", "en")]
    public async Task A_created_filters_catalog_takes_its_original_language_from_the_menu(string uiCulture, string original)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiCulture);
        try
        {
            var repository = new FormCatalogCapacityRepository();

            var result = await FiltersService(repository, new FakeUnitOfWork()).CreateAsync(new(Texts(), Texts(),
                new("name", null, [], false, "name", false)), Ct);

            Assert.True(result.IsSuccess);
            Assert.Equal(BusinessFormCatalogKind.Filters, repository.Catalog!.Kind);
            Assert.Equal(original, repository.Catalog.OriginalCulture);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public async Task Text_is_the_default_and_an_empty_typed_catalog_can_be_saved()
    {
        foreach (var type in new string?[] { null, "Text", "Integer", "Decimal", "Date" })
        {
            var repository = new FormCatalogCapacityRepository();
            var work = new FakeUnitOfWork();
            var result = await Service(repository, work).CreateManualAsync(new(Texts(), Texts(), [], type), Ct);
            Assert.True(result.IsSuccess);
            Assert.Equal(type ?? "Text", result.Value.Catalog.ValueType);
            Assert.Empty(repository.Items);
            Assert.Equal(1, work.Commits);
        }
    }

    [Fact]
    public async Task Reordering_and_renaming_keep_the_catalog_type_and_each_saved_value()
    {
        var metadata = new Dictionary<string, string> { ["es"] = "Zonas", ["en"] = "Areas" };
        var catalog = BusinessFormCatalog.CreateManual("es", metadata, metadata, BusinessFormCatalogValueType.Decimal);
        var first = catalog.AddManualItem("ONE", "Uno", null, null, null, 1, "1500").Value;
        var second = catalog.AddManualItem("TWO", "Dos", null, null, null, 2, "0.1234567890123456789012345678").Value;
        var repository = new FormCatalogCapacityRepository(catalog, [first, second]);
        var work = new FakeUnitOfWork();
        var result = await Service(repository, work).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], [new(first.Id, "down")]), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("Decimal", result.Value.Catalog.ValueType);
        Assert.Equal("1500", first.Value);
        Assert.Equal("0.1234567890123456789012345678", second.Value);
        Assert.Equal(2, first.Position);
        Assert.Equal(1, second.Position);
        Assert.Equal(2, catalog.Revision);
        Assert.Equal(1, work.Commits);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static Dictionary<string, string?> Texts() => new() { ["es"] = "Zonas", ["en"] = "Areas" };
    private static FormCatalogManualItemSaveRequest Input(string? value) => new("row-1", null, "GBA",
        new Dictionary<string, string?> { ["es"] = "GBA" }, null, true, value);
    private static FormCatalogService Service(FormCatalogCapacityRepository repository, FakeUnitOfWork work)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        return new(repository, null!, work,
            RequestValidators.For(new CreateManualCatalogRequestValidator(), new UpdateManualCatalogRequestValidator()),
            new(new FakeCurrentUser { UserId = actor }, permissions), null!, new(repository), new(null!));
    }
    private static FormCatalogFiltersService FiltersService(FormCatalogCapacityRepository repository, FakeUnitOfWork work)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        return new(repository, null!, work, RequestValidators.For(new CreateFiltersCatalogRequestValidator()),
            new(new FakeCurrentUser { UserId = actor }, permissions), null!, null!);
    }
}
