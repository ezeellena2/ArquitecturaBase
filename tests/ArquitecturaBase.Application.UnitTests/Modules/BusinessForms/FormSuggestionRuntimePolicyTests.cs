using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormSuggestionRuntimePolicyTests
{
    [Fact]
    public async Task Always_lists_the_own_captured_source_and_maps_to_a_published_target_without_general_instructions()
    {
        var f = new FormSuggestionRuntimeFixture();
        var result = await f.Policy.ListAsync(f.Assistant.UserId, "es", f.SourceId, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var candidate = Assert.Single(result.Value.Candidates);
        Assert.Equal("Always", candidate.Mode); Assert.Null(candidate.When);
        Assert.Equal(f.Assistant.Catalog.Items[0].Id.ToString("D"), Assert.Single(candidate.Fields).Value);
        Assert.Equal(f.Set.Id, result.Value.SuggestionSetId); Assert.Equal(f.SourceId, result.Value.SourceRecordId);
    }

    [Fact]
    public async Task Suggestion_source_values_omit_sealed_catalog_fields_like_the_chat()
    {
        var f = new FormSuggestionRuntimeFixture();
        var row = f.Responses.ExportRows[0];
        var reference = row.Record.References![0] with { FieldValuesJson = FormDraftPolicy.Serialize(new[] { new FormCatalogCapturedField(Guid.NewGuid(), "tax_id",
            new Dictionary<string, string> { ["es"] = "CUIT", ["en"] = "Tax ID" }, "text", "data", JsonSerializer.SerializeToElement("30712345678")) }) };
        f.Responses.ExportRows = [row with { Record = row.Record with { References = [reference] } }];
        var result = await f.Candidates.ListAsync(f.Assistant.UserId, "es", f.SourceId, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var choice = Assert.IsType<StructuredRecordChoiceValue>(Assert.Single(result.Value.SourceValues).Value);
        Assert.Equal(reference.ItemId.ToString("D"), choice.ChoiceId);
        Assert.Null(choice.Fields);
    }

    [Fact]
    public async Task Missing_recipient_permission_fails_before_disclosing_the_source()
    {
        var f = new FormSuggestionRuntimeFixture(); f.Assistant.Permissions.Permissions.Clear();
        var result = await f.Policy.ListAsync(f.Assistant.UserId, "es", f.SourceId, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure); Assert.Equal(FormRecordErrors.AuthorizationChanged, result.Error); Assert.Equal(0, f.Responses.Reads);
    }

    [Fact]
    public async Task A_source_captured_by_another_person_is_unavailable()
    {
        var f = new FormSuggestionRuntimeFixture(); f.Responses.ExportRows = [f.Responses.ExportRows[0] with { Actor = new(Guid.NewGuid(), "Other") }];
        var result = await f.Policy.ListAsync(f.Assistant.UserId, "es", f.SourceId, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure); Assert.Equal(FormRecordErrors.AuthorizationChanged, result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inactive_rows_and_unmet_conditions_are_not_offered(bool condition)
    {
        var f = new FormSuggestionRuntimeFixture();
        f.Save([f.Suggestion with { IsActive = condition, Conditions = condition ? [new(f.SourceFieldId, "unanswered", null)] : [] }]);
        var result = await f.Policy.ListAsync(f.Assistant.UserId, "es", f.SourceId, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.Empty(result.Value.Candidates);
    }

    [Fact]
    public async Task A_target_that_is_not_published_is_not_offered()
    {
        var f = new FormSuggestionRuntimeFixture(); f.Suggestions.Options = f.Suggestions.Options! with { Destinations = [] };
        var result = await f.Policy.ListAsync(f.Assistant.UserId, "es", f.SourceId, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.Empty(result.Value.Candidates);
    }

    [Fact]
    public async Task An_unpreparable_target_is_excluded_from_the_configured_offers()
    {
        var f = new FormSuggestionRuntimeFixture();
        for (var i = 1; i < 202; i++) f.Assistant.Catalog.Items.Add(BusinessFormCatalogItem.Create(f.Assistant.Catalog.Catalog.Id,
            "P" + i, "Persona " + i, "Person " + i, null, null, i));
        var result = await f.Policy.ListAsync(f.Assistant.UserId, "es", f.SourceId, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Candidates);
    }

    [Fact]
    public async Task An_inactive_catalog_excludes_only_its_target_and_keeps_the_other_configured_suggestion()
    {
        var f = new FormSuggestionRuntimeFixture();
        var original = f.Assistant.Rows[0];
        var schema = FormDraftPolicy.Read<FormVersionSchema>(original.SchemaJson);
        var scalar = schema.Questions[0] with { Type = "shortText", Source = null, Options = null, MaxLength = 80 };
        var good = original with { DefinitionId = Guid.NewGuid(), VersionId = Guid.NewGuid(), SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [scalar])) };
        f.Assistant.Rows.Add(good);
        f.Suggestions.Options = f.Suggestions.Options! with { Destinations = [.. f.Suggestions.Options!.Destinations,
            new(good.DefinitionId, good.VersionId, FormDraftPolicy.Read<Dictionary<string, string>>(good.NameJson), good.Culture, [scalar])] };
        var second = f.Suggestion with { Id = Guid.NewGuid(), DestinationDefinitionId = good.DefinitionId, DestinationVersionId = good.VersionId, Mappings = [] };
        f.Save([f.Suggestion, second]);
        Assert.True(f.Assistant.Catalog.Catalog.SetActive(f.Assistant.Catalog.Catalog.Revision, false).IsSuccess);
        var result = await f.Policy.ListAsync(f.Assistant.UserId, "es", f.SourceId, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(second.Id, Assert.Single(result.Value.Candidates).SuggestionId);
    }

    [Fact]
    public async Task Always_preparation_uses_no_model_proposal_and_returns_the_real_mapping_origin()
    {
        var f = new FormSuggestionRuntimeFixture();
        var result = await f.Policy.PrepareAsync(f.Request, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(f.SourceId, result.Value.OriginSuggestion.OriginRecordId);
        Assert.Equal(f.Suggestion.Id, result.Value.OriginSuggestion.OriginSuggestionId);
        Assert.Equal(f.Set.Id, result.Value.OriginSuggestion.SuggestionSetId);
        Assert.Single(result.Value.Prefill.Fields); Assert.Single(result.Value.OriginSuggestion.Mappings);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Prefill.Reason));
    }

    [Fact]
    public async Task Operator_exclusion_keeps_the_proposal_but_sends_no_prefilled_value()
    {
        var f = new FormSuggestionRuntimeFixture();
        var result = await f.Policy.PrepareAsync(f.Request with { IncludedFieldIds = [] }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.Empty(result.Value.Prefill.Fields);
        Assert.Single(result.Value.OriginSuggestion.Mappings);
    }

    [Fact]
    public async Task Unknown_included_fields_are_rejected()
    {
        var f = new FormSuggestionRuntimeFixture();
        Assert.True((await f.Policy.PrepareAsync(f.Request with { IncludedFieldIds = [Guid.NewGuid()] }, TestContext.Current.CancellationToken)).IsFailure);
    }

    [Fact]
    public async Task Assistant_mode_cannot_be_accepted_without_an_authentic_proposal()
    {
        var f = new FormSuggestionRuntimeFixture("Assistant");
        Assert.True((await f.Policy.PrepareAsync(f.Request, TestContext.Current.CancellationToken)).IsFailure);
    }

    [Fact]
    public async Task A_configuration_changed_after_preview_returns_a_conflict()
    {
        var f = new FormSuggestionRuntimeFixture(); var request = f.Request; f.Save([]);
        var result = await f.Policy.PrepareAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure); Assert.Equal(FormSuggestionErrors.RevisionConflict, result.Error);
    }

    [Fact]
    public async Task Republished_target_uses_the_current_prepared_version_instead_of_the_configurations_historical_label_version()
    {
        var f = new FormSuggestionRuntimeFixture(); var next = Guid.NewGuid();
        f.Assistant.Rows[0] = f.Assistant.Rows[0] with { VersionId = next, DefinitionRevision = 4 };
        f.Suggestions.Options = f.Suggestions.Options! with { Destinations = [f.Suggestions.Options!.Destinations[0] with { VersionId = next }] };
        var result = await f.Policy.PrepareAsync(f.Request, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.Equal(next, result.Value.Prefill.Form.VersionId); Assert.Equal(4, result.Value.Prefill.DefinitionRevision);
    }

    [Fact]
    public async Task A_previous_definition_is_marked_as_an_automatic_loop_without_blocking_manual_preparation()
    {
        var f = new FormSuggestionRuntimeFixture(); f.Responses.AncestorDefinitionIds = [f.Assistant.Rows[0].DefinitionId];
        var result = await f.Policy.PrepareAsync(f.Request, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.True(result.Value.CreatesAutomaticLoop);
    }
}
