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
        reader.Catalogs[catalog.Id] = Row(catalog);
        reader.Items[catalog.Id] = [new(item.Id, catalog.Id, item.Code, item.TextEs, null, null, null, 1, true, FieldValuesJson: fieldValues)];
        var result = await Service(catalog, reader, new()).GetAsync(catalog.Id, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(result.Value.Fields!).HasValues);
        Assert.Equal(["es", "en"], result.Value.PresentationCheck!.Cultures.Select(check => check.Culture));
        Assert.All(result.Value.PresentationCheck.Cultures, check =>
        {
            Assert.Equal(1, check.TitleTooLongCount);
            Assert.Equal(item.Id, Assert.Single(check.TitleExamples).ItemId);
        });
    }

    [Fact]
    public async Task Detail_checks_titles_and_descriptions_in_each_language_the_catalog_has()
    {
        var ct = TestContext.Current.CancellationToken;
        var presentation = JsonSerializer.Serialize(new FormCatalogPresentation(["text"], ["detail"]), JsonSerializerOptions.Web);
        var longEnglish = new string('E', 31);
        var longDetail = new string('d', 301);
        var catalog = Catalog();
        Assert.True(catalog.SetFields([], presentation).IsSuccess);
        var active = Guid.CreateVersion7();
        var reader = new FormCatalogFieldReader();
        reader.Catalogs[catalog.Id] = Row(catalog);
        reader.Items[catalog.Id] =
        [
            new(active, catalog.Id, "one", "Uno", longEnglish, longDetail, null, 0, true),
            new(Guid.CreateVersion7(), catalog.Id, "two", new string('S', 31), new string('E', 31), longDetail, longDetail, 1, false),
        ];

        var result = await Service(catalog, reader, new()).GetAsync(catalog.Id, ct);

        Assert.True(result.IsSuccess);
        var cultures = result.Value.PresentationCheck!.Cultures;
        Assert.Equal(["es", "en"], cultures.Select(check => check.Culture));
        var spanish = cultures[0];
        Assert.Equal(0, spanish.TitleTooLongCount);
        Assert.Empty(spanish.TitleExamples);
        Assert.Equal(1, spanish.DescriptionTooLongCount);
        Assert.Equal(new FormCatalogPresentationExample(active, "Uno"), Assert.Single(spanish.DescriptionExamples));
        var english = cultures[1];
        Assert.Equal(1, english.TitleTooLongCount);
        Assert.Equal(new FormCatalogPresentationExample(active, longEnglish), Assert.Single(english.TitleExamples));
        Assert.Equal(1, english.DescriptionTooLongCount);
        Assert.Equal(new FormCatalogPresentationExample(active, longEnglish), Assert.Single(english.DescriptionExamples));

        var spanishOnly = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Catálogo" },
            new Dictionary<string, string> { ["es"] = "Descripción" });
        Assert.True(spanishOnly.SetFields([], presentation).IsSuccess);
        reader.Catalogs[spanishOnly.Id] = Row(spanishOnly);
        reader.Items[spanishOnly.Id] = [new(Guid.CreateVersion7(), spanishOnly.Id, "one", "Uno", null, longDetail, null, 0, true)];

        var single = await Service(spanishOnly, reader, new()).GetAsync(spanishOnly.Id, ct);

        Assert.True(single.IsSuccess);
        var only = Assert.Single(single.Value.PresentationCheck!.Cultures);
        Assert.Equal("es", only.Culture);
        Assert.Equal(1, only.DescriptionTooLongCount);
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

    [Fact]
    public async Task Field_uses_show_form_names_only_to_actors_who_can_read_forms()
    {
        var (catalog, field) = OneFieldCatalog();
        var reader = new FormCatalogFieldReader();
        reader.Catalogs[catalog.Id] = new(catalog.Id, catalog.Key, catalog.NameJson, catalog.DescriptionJson, catalog.OriginalCulture, "Manual", 1, true,
            catalog.CreatedAtUtc, null, null, null, 0, FieldsJson: catalog.FieldsJson);
        var (definitionId, supplier, zone) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        reader.Uses.Add(new(definitionId, "Pedir repuesto", "Proveedor", supplier, "Draft", field.Id, ByMatch: true));
        reader.Uses.Add(new(definitionId, "Pedir repuesto", "Zona", zone, "Draft", field.Id, ByMatch: false));
        reader.Uses.Add(new(definitionId, "Pedir repuesto", "Proveedor", supplier, "Active", field.Id, ByMatch: true));

        var hidden = await Service(catalog, reader, new()).GetAsync(catalog.Id, TestContext.Current.CancellationToken);
        var named = await Service(catalog, reader, new(), canReadForms: true).GetAsync(catalog.Id, TestContext.Current.CancellationToken);

        // Without formtemplates.read the field is still in use, once per form and stage, but nothing names the form or the question.
        var redacted = Assert.Single(hidden.Value.Fields!).UsedBy;
        Assert.Equal(new[] { "Active", "Draft" }, redacted.Select(use => use.Stage));
        Assert.All(redacted, AssertRedacted);
        var uses = Assert.Single(named.Value.Fields!).UsedBy;
        Assert.Equal(3, uses.Count);
        Assert.All(uses, use => Assert.Equal((definitionId, "Pedir repuesto"), (use.DefinitionId, use.FormName)));
    }

    [Fact]
    public async Task A_field_in_use_conflict_hides_form_names_without_permission_to_read_forms()
    {
        var (catalog, field) = OneFieldCatalog();
        var reader = new FormCatalogFieldReader();
        var definitionId = Guid.CreateVersion7();
        reader.Uses.Add(new(definitionId, "Pedir repuesto", "Proveedor", Guid.CreateVersion7(), "Draft", field.Id, ByMatch: true));
        reader.Uses.Add(new(definitionId, "Pedir repuesto", "Zona", Guid.CreateVersion7(), "Draft", field.Id, ByMatch: false));
        var work = new FakeUnitOfWork();
        var removal = new FormCatalogFieldSaveRequest(null, field.Id, field.Key, new Dictionary<string, string?> { ["es"] = "Field" }, field.Type, null,
            false, false, null, null, 300, "data", 0, false);

        var result = await Service(catalog, reader, work).UpdateManualAsync(catalog.Id, new(catalog.Revision, Texts(), Texts(), [], Fields: [removal]),
            TestContext.Current.CancellationToken);

        Assert.Equal("Forms.Catalog.FieldInUse", result.Error.Code);
        var use = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<FormCatalogFieldUsageResponse>>(result.Error.Metadata!["uses"]));
        Assert.Equal("Draft", use.Stage);
        AssertRedacted(use);
        Assert.Equal(0, work.Commits);
    }

    private static void AssertRedacted(FormCatalogFieldUsageResponse use)
    {
        Assert.Null(use.DefinitionId);
        Assert.Null(use.FormName);
        Assert.Null(use.Question);
        Assert.Null(use.QuestionId);
    }

    private static (BusinessFormCatalog Catalog, BusinessFormCatalogField Field) OneFieldCatalog()
    {
        var catalog = Catalog();
        var field = new BusinessFormCatalogField(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Field" }, "text", null,
            false, false, null, null, 300, "data", 0, true);
        Assert.True(catalog.SetFields([field], null).IsSuccess);
        return (catalog, field);
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
        IFormCatalogRepository? items = null, bool canReadForms = false)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = canReadForms
            ? [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage, BusinessFormsPermissions.Read]
            : [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        var repository = items ?? new FormCatalogActivationRepository(catalog);
        return new(repository, reader, work, RequestValidators.For(new UpdateManualCatalogRequestValidator()),
            new FormCatalogAccessGuard(new FakeCurrentUser { UserId = actor }, permissions), null!, new(repository), new(reader));
    }

    private static FormCatalogStoredRow Row(BusinessFormCatalog catalog) => new(catalog.Id, catalog.Key, catalog.NameJson, catalog.DescriptionJson,
        catalog.OriginalCulture, "Manual", 1, true, catalog.CreatedAtUtc, null, null, null, 1, FieldsJson: catalog.FieldsJson,
        PresentationJson: catalog.PresentationJson);

    private static BusinessFormCatalog Catalog() => BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Catalog", ["en"] = "Catalog" }, new Dictionary<string, string> { ["es"] = "Description", ["en"] = "Description" });
    private static Dictionary<string, string?> Texts() => new() { ["es"] = "Catalog", ["en"] = "Catalog" };
}
