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
        var result = await new FormCatalogFieldRecorder(new FormCatalogFieldReader()).PrepareAsync(catalog, [], [Input(null)], null, [], true, TestContext.Current.CancellationToken);
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
        var rejected = await recorder.PrepareAsync(catalog, [item], [input], null, [], true, TestContext.Current.CancellationToken);
        Assert.Equal("Forms.Catalog.FieldRemovalNotConfirmed", rejected.Error.Code);
        Assert.Equal(1, rejected.Error.Metadata!["count"]);
        Assert.True((await recorder.PrepareAsync(catalog, [item], [input], null, [field.Id], true, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.False(Assert.Single(FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson)).IsActive);
    }

    [Fact]
    public async Task A_field_used_by_an_active_or_draft_question_cannot_be_removed()
    {
        var catalog = Catalog();
        var field = Field();
        Assert.True(catalog.SetFields([field], null).IsSuccess);
        var reader = new FormCatalogFieldReader();
        reader.Uses.Add(new(Guid.CreateVersion7(), "Form", "Question", Guid.CreateVersion7(), "Active", field.Id, ByMatch: true));
        var result = await new FormCatalogFieldRecorder(reader).PrepareAsync(catalog, [], [Input(field.Id) with { IsActive = false }], null, [], true, TestContext.Current.CancellationToken);
        Assert.Equal("Forms.Catalog.FieldInUse", result.Error.Code);
        Assert.Equal(ArquitecturaBase.Domain.Results.ErrorType.Conflict, result.Error.Type);
        Assert.NotNull(result.Error.Metadata!["uses"]);
        Assert.True(Assert.Single(FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson)).IsActive);
    }

    [Fact]
    public async Task A_field_in_use_reports_the_question_identity_and_stage_of_each_use()
    {
        var catalog = Catalog();
        var field = Field();
        Assert.True(catalog.SetFields([field], null).IsSuccess);
        var definitionId = Guid.CreateVersion7();
        var questionId = Guid.CreateVersion7();
        var reader = new FormCatalogFieldReader();
        reader.Uses.Add(new(definitionId, "Form", "Question", questionId, "Active", field.Id, ByMatch: true));
        reader.Uses.Add(new(definitionId, "Form", "Question", questionId, "Draft", field.Id, ByMatch: true));

        var result = await new FormCatalogFieldRecorder(reader).PrepareAsync(catalog, [], [Input(field.Id) with { IsActive = false }], null, [], true, TestContext.Current.CancellationToken);

        Assert.Equal("Forms.Catalog.FieldInUse", result.Error.Code);
        Assert.Equal(
            [new FormCatalogFieldUsageResponse(definitionId, "Form", "Question", questionId, "Active"),
                new FormCatalogFieldUsageResponse(definitionId, "Form", "Question", questionId, "Draft")],
            Assert.IsAssignableFrom<IReadOnlyList<FormCatalogFieldUsageResponse>>(result.Error.Metadata!["uses"]));
    }

    [Theory]
    [InlineData("role")]
    [InlineData("type")]
    [InlineData("source")]
    public async Task Changing_the_role_type_or_source_of_a_field_used_by_a_filter_is_a_conflict_with_only_its_filter_uses(string change)
    {
        var (catalog, field, reader) = FilterField();
        var stored = catalog.FieldsJson;
        var filterQuestionId = Guid.CreateVersion7();
        reader.Uses.Add(new(Guid.CreateVersion7(), "Form", "Question", filterQuestionId, "Draft", field.Id, ByMatch: true));
        // A question that only shows the field does not block the change, so the conflict leaves it out.
        reader.Uses.Add(new(Guid.CreateVersion7(), "Form", "Shown", Guid.CreateVersion7(), "Active", field.Id, ByMatch: false));
        var input = FilterInput(field);
        var (edited, property) = change switch
        {
            "role" => (input with { Role = "data" }, "role"),
            "type" => (input with { Type = "text", SourceCatalogId = null, MaxLength = 300 }, "type"),
            _ => (input with { SourceCatalogId = reader.Catalogs.Keys.Single(id => id != field.SourceCatalogId) }, "sourceCatalogId"),
        };

        var result = await new FormCatalogFieldRecorder(reader).PrepareAsync(catalog, [], [edited], null, [], true, TestContext.Current.CancellationToken);

        Assert.Equal("Forms.Catalog.FieldInUse", result.Error.Code);
        Assert.Equal(ArquitecturaBase.Domain.Results.ErrorType.Conflict, result.Error.Type);
        Assert.Equal($"fields[0].{property}", Assert.Single(Assert.IsType<ArquitecturaBase.Domain.Results.ValidationError>(result.Error).Errors.Keys));
        var use = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<FormCatalogFieldUsageResponse>>(result.Error.Metadata!["uses"]));
        Assert.Equal((filterQuestionId, "Draft"), (use.QuestionId, use.Stage));
        Assert.Equal(stored, catalog.FieldsJson);
    }

    [Fact]
    public async Task A_field_shown_only_by_a_question_presentation_can_change_its_role()
    {
        var (catalog, field, reader) = FilterField();
        reader.Uses.Add(new(Guid.CreateVersion7(), "Form", "Question", Guid.CreateVersion7(), "Draft", field.Id, ByMatch: false));

        var result = await new FormCatalogFieldRecorder(reader).PrepareAsync(catalog, [], [FilterInput(field) with { Role = "data" }], null, [], true, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("data", Assert.Single(FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson)).Role);
    }

    [Fact]
    public async Task A_catalog_presentation_must_only_reference_active_existing_fields()
    {
        var catalog = Catalog();
        var result = await new FormCatalogFieldRecorder(new FormCatalogFieldReader()).PrepareAsync(catalog, [], [], new([Guid.CreateVersion7().ToString()], []), [], true, TestContext.Current.CancellationToken);
        Assert.Equal("Forms.Catalog.PresentationInvalid", result.Error.Code);
        Assert.Null(catalog.FieldsJson);
        Assert.Null(catalog.PresentationJson);
    }

    [Fact]
    public async Task Field_bounds_are_stored_canonically_without_changing_their_meaning()
    {
        var catalog = Catalog();
        var input = Input(null) with { Type = "integer", MaxLength = null, Min = "0010", Max = "0020" };
        var result = await new FormCatalogFieldRecorder(new FormCatalogFieldReader()).PrepareAsync(catalog, [], [input], null, [], true, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var field = Assert.Single(FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson));
        Assert.Equal("10", field.Min);
        Assert.Equal("20", field.Max);
    }

    [Theory]
    [InlineData("integer", "abc", null, "min")]
    [InlineData("integer", null, "abc", "max")]
    [InlineData("integer", "10", "5", "max")]
    [InlineData("text", "1", null, "type")]
    public async Task Invalid_bounds_are_reported_on_the_control_that_holds_them(string type, string? min, string? max, string control)
    {
        var catalog = Catalog();
        var input = Input(null) with { Type = type, MaxLength = type == "text" ? 300 : null, Min = min, Max = max };

        var result = await new FormCatalogFieldRecorder(new FormCatalogFieldReader()).PrepareAsync(catalog, [], [input], null, [], true, TestContext.Current.CancellationToken);

        Assert.Equal(FormCatalogErrors.FieldInvalid.Code, result.Error.Code);
        Assert.Equal($"fields[0].{control}", Assert.Single(Assert.IsType<ArquitecturaBase.Domain.Results.ValidationError>(result.Error).Errors.Keys));
        Assert.Null(catalog.FieldsJson);
    }

    [Fact]
    public async Task Converting_a_catalog_field_loads_its_previous_source_with_inactive_options()
    {
        var catalog = Catalog();
        var oldSource = Guid.CreateVersion7();
        var field = Field() with { Type = "catalog", SourceCatalogId = oldSource, MaxLength = null };
        Assert.True(catalog.SetFields([field], null).IsSuccess);
        var reader = new FormCatalogFieldReader();
        var north = new FormCatalogStoredItemRow(Guid.CreateVersion7(), oldSource, "N", "Norte", "North", null, null, 1, false);
        reader.Items[oldSource] = [north];

        var result = await new FormCatalogFieldRecorder(reader).PrepareAsync(catalog, [], [Input(field.Id)], null, [], true, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Context.Sources.TryGetValue(oldSource, out var options));
        Assert.Equal(north, Assert.Single(options));
    }

    [Fact]
    public async Task A_type_change_on_an_inactive_field_to_catalog_loads_its_source()
    {
        var catalog = Catalog();
        var field = Field() with { IsActive = false };
        Assert.True(catalog.SetFields([field], null).IsSuccess);
        var reader = new FormCatalogFieldReader();
        var sourceId = Guid.CreateVersion7();
        reader.Catalogs[sourceId] = new(sourceId, "source", """{"es":"Source"}""", "{}", "es", "Manual", 1, true, DateTime.UnixEpoch, null, null, null, 0);
        var north = new FormCatalogStoredItemRow(Guid.CreateVersion7(), sourceId, "N", "Norte", null, null, null, 1, true);
        reader.Items[sourceId] = [north];
        var input = Input(field.Id) with { IsActive = false, Type = "catalog", SourceCatalogId = sourceId, MaxLength = null };

        var result = await new FormCatalogFieldRecorder(reader).PrepareAsync(catalog, [], [input], null, [], true, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Context.Sources.TryGetValue(sourceId, out var options));
        Assert.Equal(north, Assert.Single(options));
    }

    private static BusinessFormCatalog Catalog() => BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
    private static BusinessFormCatalogField Field() => new(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Field" }, "text", null, false, false, null, null, 300, "data", 0, true);
    // A catalog-type filter field whose source and a second source are both active Manual catalogs, so only the use check can stop the change.
    private static (BusinessFormCatalog Catalog, BusinessFormCatalogField Field, FormCatalogFieldReader Reader) FilterField()
    {
        var catalog = Catalog();
        var reader = new FormCatalogFieldReader();
        foreach (var source in new[] { Guid.CreateVersion7(), Guid.CreateVersion7() })
            reader.Catalogs[source] = new(source, "source-" + source.ToString("N"), """{"es":"Source"}""", "{}", "es", "Manual", 1, true, DateTime.UnixEpoch, null, null, null, 0);
        var field = Field() with { Type = "catalog", SourceCatalogId = reader.Catalogs.Keys.First(), MaxLength = null, Role = "filter" };
        Assert.True(catalog.SetFields([field], null).IsSuccess);
        return (catalog, field, reader);
    }
    private static FormCatalogFieldSaveRequest FilterInput(BusinessFormCatalogField field) => Input(field.Id) with
    { Type = field.Type, SourceCatalogId = field.SourceCatalogId, MaxLength = field.MaxLength, Role = field.Role };
    private static FormCatalogFieldSaveRequest Input(Guid? id) => new(id is null ? "local" : null, id, "field", new Dictionary<string, string?> { ["es"] = "Field" }, "text", null, false, false, null, null, 300, "data", 0, true);
}
