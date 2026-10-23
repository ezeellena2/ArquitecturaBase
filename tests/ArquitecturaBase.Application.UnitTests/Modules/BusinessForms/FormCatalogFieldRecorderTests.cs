using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogFieldRecorderTests
{
    [Fact]
    public async Task New_field_ids_come_from_the_server_and_map_back_to_local_identity()
    {
        var catalog = Catalog();
        var result = await new FormCatalogFieldRecorder(new FormCatalogFieldReader()).PrepareAsync(catalog, [], [Input(null)], null, [], TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var id = result.Value.IdentityMap["local"];
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(id, Assert.Single(FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson)).Id);
        Assert.Equal(1, catalog.Revision);
    }

    [Fact]
    public async Task Removing_a_field_with_values_needs_explicit_confirmation()
    {
        var catalog = Catalog();
        var field = Field();
        Assert.True(catalog.SetFields([field], null).IsSuccess);
        var item = catalog.AddManualItem("one", "One", null, null, null, 1).Value;
        Assert.True(catalog.SetManualItemFields(item, JsonSerializer.Serialize(new Dictionary<string, string> { [field.Id.ToString()] = "value" })).IsSuccess);
        var recorder = new FormCatalogFieldRecorder(new FormCatalogFieldReader());
        var input = Input(field.Id) with { IsActive = false };
        var rejected = await recorder.PrepareAsync(catalog, [item], [input], null, [], TestContext.Current.CancellationToken);
        Assert.Equal("Forms.Catalog.FieldRemovalNotConfirmed", rejected.Error.Code);
        Assert.Equal(1, rejected.Error.Metadata!["count"]);
        Assert.True((await recorder.PrepareAsync(catalog, [item], [input], null, [field.Id], TestContext.Current.CancellationToken)).IsSuccess);
        Assert.False(Assert.Single(FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson)).IsActive);
    }

    [Fact]
    public async Task A_field_used_by_an_active_or_draft_question_cannot_be_removed()
    {
        var catalog = Catalog();
        var field = Field();
        Assert.True(catalog.SetFields([field], null).IsSuccess);
        var reader = new FormCatalogFieldReader();
        reader.Uses.Add(new(Guid.CreateVersion7(), "Form", "Question", "Active"));
        var result = await new FormCatalogFieldRecorder(reader).PrepareAsync(catalog, [], [Input(field.Id) with { IsActive = false }], null, [], TestContext.Current.CancellationToken);
        Assert.Equal("Forms.Catalog.FieldInUse", result.Error.Code);
        Assert.Equal(ArquitecturaBase.Domain.Results.ErrorType.Conflict, result.Error.Type);
        Assert.NotNull(result.Error.Metadata!["uses"]);
        Assert.True(Assert.Single(FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson)).IsActive);
    }

    [Fact]
    public async Task A_catalog_presentation_must_only_reference_active_existing_fields()
    {
        var catalog = Catalog();
        var result = await new FormCatalogFieldRecorder(new FormCatalogFieldReader()).PrepareAsync(catalog, [], [], new([Guid.CreateVersion7().ToString()], []), [], TestContext.Current.CancellationToken);
        Assert.Equal("Forms.Catalog.PresentationInvalid", result.Error.Code);
        Assert.Null(catalog.FieldsJson);
        Assert.Null(catalog.PresentationJson);
    }

    [Fact]
    public async Task Field_bounds_are_stored_canonically_without_changing_their_meaning()
    {
        var catalog = Catalog();
        var input = Input(null) with { Type = "integer", MaxLength = null, Min = "0010", Max = "0020" };
        var result = await new FormCatalogFieldRecorder(new FormCatalogFieldReader()).PrepareAsync(catalog, [], [input], null, [], TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var field = Assert.Single(FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson));
        Assert.Equal("10", field.Min);
        Assert.Equal("20", field.Max);
    }

    private static BusinessFormCatalog Catalog() => BusinessFormCatalog.CreateManual(new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
    private static BusinessFormCatalogField Field() => new(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Field" }, "text", null, false, false, null, null, 300, "data", 0, true);
    private static FormCatalogFieldSaveRequest Input(Guid? id) => new(id is null ? "local" : null, id, "field", new Dictionary<string, string?> { ["es"] = "Field" }, "text", null, false, false, null, null, 300, "data", 0, true);
}
