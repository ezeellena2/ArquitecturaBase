using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogFieldServiceTests
{
    [Fact]
    public async Task Saving_fields_returns_server_identity_and_the_same_single_catalog_revision()
    {
        var catalog = Catalog();
        var work = new FakeUnitOfWork();
        var reader = new FormCatalogFieldReader();
        var field = new FormCatalogFieldSaveRequest("local", null, "field", Texts(), "text", null, false, false, null, null, 300, "data", 0, true);
        var result = await Service(catalog, reader, work).UpdateManualAsync(catalog.Id, new(1, Texts(), Texts(), [], Fields: [field]), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var id = result.Value.FieldIdentityMap!["local"];
        Assert.Equal(id, Assert.Single(result.Value.Catalog.Fields!).Id);
        Assert.Equal(2, result.Value.Catalog.Revision);
        Assert.Equal(1, work.Commits);
    }

    [Fact]
    public async Task A_catalog_used_as_a_value_source_cannot_be_deactivated()
    {
        var catalog = Catalog();
        var work = new FakeUnitOfWork();
        var result = await Service(catalog, new FormCatalogFieldReader { SourceInUse = true }, work).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [], IsActive: false), TestContext.Current.CancellationToken);
        Assert.Equal("Forms.Catalog.SourceInUse", result.Error.Code);
        Assert.True(catalog.IsActive);
        Assert.Equal(1, catalog.Revision);
        Assert.Equal(0, work.Commits);
    }

    [Fact]
    public async Task An_item_cannot_store_values_for_an_unknown_field()
    {
        var catalog = Catalog();
        var id = Guid.CreateVersion7().ToString("D");
        var item = new FormCatalogManualItemSaveRequest("item", null, "one", Texts(), null, true, Fields: new Dictionary<string, string?> { [id] = "value" });
        var result = await Service(catalog, new FormCatalogFieldReader(), new()).UpdateManualAsync(catalog.Id,
            new(1, Texts(), Texts(), [item]), TestContext.Current.CancellationToken);
        Assert.Equal("Forms.Catalog.FieldInvalid", result.Error.Code);
    }

    [Fact]
    public async Task Detail_checks_composed_titles_without_rejecting_the_saved_catalog()
    {
        var catalog = Catalog();
        var field = new BusinessFormCatalogField(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Field" }, "text", null, false, false, null, null, 300, "data", 0, true);
        Assert.True(catalog.SetFields([field], JsonSerializer.Serialize(new FormCatalogPresentation(["text", field.Id.ToString()], []), JsonSerializerOptions.Web)).IsSuccess);
        var item = catalog.AddManualItem("one", "One", null, null, null, 1).Value;
        var fieldValues = JsonSerializer.Serialize(new Dictionary<string, string> { [field.Id.ToString()] = new string('x', 30) });
        var reader = new FormCatalogFieldReader();
        reader.Catalogs[catalog.Id] = new(catalog.Id, catalog.Key, catalog.NameJson, catalog.DescriptionJson, "Manual", 1, true,
            catalog.CreatedAtUtc, null, null, null, 1, FieldsJson: catalog.FieldsJson, PresentationJson: catalog.PresentationJson);
        reader.Items[catalog.Id] = [new(item.Id, catalog.Id, item.Code, item.TextEs, null, null, null, 1, true, FieldValuesJson: fieldValues)];
        var result = await Service(catalog, reader, new()).GetAsync(catalog.Id, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(result.Value.Fields!).HasValues);
        Assert.Equal(1, result.Value.PresentationCheck!.TitleTooLongCount);
        Assert.Equal(item.Id, Assert.Single(result.Value.PresentationCheck.Examples).ItemId);
    }

    [Fact]
    public async Task An_item_update_that_omits_a_field_keeps_its_stored_value()
    {
        var (catalog, item, first, second) = TwoFieldCatalog();
        var update = new FormCatalogManualItemSaveRequest("item", item.Id, item.Code, Texts(), null, true,
            Fields: new Dictionary<string, string?> { [first] = "new" });

        var result = await Service(catalog, new FormCatalogFieldReader(), new(), new FormCatalogCapacityRepository(catalog, [item]))
            .UpdateManualAsync(catalog.Id, new(catalog.Revision, Texts(), Texts(), [update]), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Code);
        var values = FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson);
        Assert.Equal("new", values[first].GetString());
        Assert.Equal("second", values[second].GetString());
    }

    [Fact]
    public async Task An_item_update_with_a_null_field_clears_it()
    {
        var (catalog, item, first, second) = TwoFieldCatalog();
        var update = new FormCatalogManualItemSaveRequest("item", item.Id, item.Code, Texts(), null, true,
            Fields: new Dictionary<string, string?> { [second] = null });

        var result = await Service(catalog, new FormCatalogFieldReader(), new(), new FormCatalogCapacityRepository(catalog, [item]))
            .UpdateManualAsync(catalog.Id, new(catalog.Revision, Texts(), Texts(), [update]), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Code);
        var values = FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson);
        Assert.Equal("first", values[first].GetString());
        Assert.False(values.ContainsKey(second));
    }

    private static (BusinessFormCatalog Catalog, BusinessFormCatalogItem Item, string First, string Second) TwoFieldCatalog()
    {
        var catalog = Catalog();
        BusinessFormCatalogField TextField(string key, int position) => new(Guid.CreateVersion7(), key, new Dictionary<string, string> { ["es"] = key },
            "text", null, false, false, null, null, 300, "data", position, true);
        var first = TextField("first", 0);
        var second = TextField("second", 1);
        Assert.True(catalog.SetFields([first, second], null).IsSuccess);
        var item = catalog.AddManualItem("one", "One", null, null, null, 1).Value;
        var (firstKey, secondKey) = (first.Id.ToString("D"), second.Id.ToString("D"));
        Assert.True(catalog.SetManualItemFields(item, JsonSerializer.Serialize(new Dictionary<string, string> { [firstKey] = "first", [secondKey] = "second" },
            JsonSerializerOptions.Web)).IsSuccess);
        return (catalog, item, firstKey, secondKey);
    }

    private static FormCatalogService Service(BusinessFormCatalog catalog, FormCatalogFieldReader reader, FakeUnitOfWork work,
        IFormCatalogRepository? items = null)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        var repository = items ?? new FormCatalogActivationRepository(catalog);
        return new(repository, reader, work, RequestValidators.For(new UpdateManualCatalogRequestValidator()),
            new FormCatalogAccessGuard(new FakeCurrentUser { UserId = actor }, permissions), null!, new(repository), new(reader));
    }

    private static BusinessFormCatalog Catalog() => BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Catalog", ["en"] = "Catalog" }, new Dictionary<string, string> { ["es"] = "Description", ["en"] = "Description" });
    private static Dictionary<string, string?> Texts() => new() { ["es"] = "Catalog", ["en"] = "Catalog" };
}
