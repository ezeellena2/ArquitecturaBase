using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormSuggestionAssistantCapabilitySourceTests
{
    [Fact]
    public void Suggestions_have_one_read_and_one_proposal_on_the_existing_engine()
    {
        var f = new FormSuggestionAssistantFixture();
        Assert.Equal(2, f.Source.Definitions.Count);
        Assert.Contains(f.Source.Definitions, definition => definition.Id == BusinessFormSuggestionAssistantCapabilitySource.CandidatesId && definition.Kind == AssistantCapabilityKind.Read);
        Assert.Contains(f.Source.Definitions, definition => definition.Id == BusinessFormSuggestionAssistantCapabilitySource.RegistrationId && definition.Kind == AssistantCapabilityKind.Proposal);
    }

    [Fact]
    public async Task Read_uses_a_confirmed_source_record_and_returns_complete_configured_assistant_candidates()
    {
        var f = new FormSuggestionAssistantFixture();
        var result = await f.Source.ExecuteAsync(new(f.TurnId, f.Context, new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 1),
            "{}", [f.Offers()]), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<FormSuggestionAssistantCandidatesPayload>(result.Value.Payload);
        Assert.Equal(f.Runtime.SourceId, payload.SourceRecordId); Assert.Equal(f.Runtime.Set.Id, payload.SuggestionSetId);
        Assert.True(payload.IsComplete); Assert.Single(payload.Suggestions);
        Assert.Equal(f.Runtime.Suggestion.When, payload.Suggestions[0].Form.OfferWhen);
        Assert.Equal(f.Runtime.Assistant.Catalog.Items[0].Id.ToString("D"), Assert.Single(payload.Suggestions[0].MappedValues).Value);
    }

    [Theory]
    [InlineData(160, true)]
    [InlineData(161, false)]
    public async Task Captured_source_labels_use_the_editors_grapheme_limit_in_discovery_and_the_codec(int graphemes, bool accepted)
    {
        var f = new FormSuggestionAssistantFixture();
        var label = string.Concat(Enumerable.Repeat("👩‍💻", graphemes));
        var captured = Assert.Single(f.Runtime.Responses.ExportRows);
        var schema = FormDraftPolicy.Read<FormVersionSchema>(captured.Record.SchemaJson);
        var texts = new Dictionary<string, string> { ["es"] = label, ["en"] = label };
        var schemaJson = FormDraftPolicy.Serialize(schema with { Questions = schema.Questions.Select(field => field with { Label = texts }).ToArray() });
        f.Runtime.Responses.ExportRows = [captured with
        {
            Record = captured.Record with { SchemaJson = schemaJson },
            Version = captured.Version! with { SchemaJson = schemaJson }
        }];

        var result = await f.Source.ExecuteAsync(new(f.TurnId, f.Context, new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 1),
            "{}", [f.Offers()]), TestContext.Current.CancellationToken);

        Assert.Equal(accepted, result.IsSuccess);
        var payload = f.ReadPayload() with { SourceValues = f.ReadPayload().SourceValues.Select(value => value with { Label = label }).ToArray() };
        if (accepted)
        {
            payload = Assert.IsType<FormSuggestionAssistantCandidatesPayload>(result.Value.Payload);
            Assert.Equal(label, Assert.Single(payload.SourceValues).Label);
        }
        else Assert.Equal(FormRecordErrors.InvalidResponse, result.Error);
        Assert.Equal(accepted, f.Source.ParseOutput(new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 1),
            FormDraftPolicy.Serialize(payload)).IsSuccess);
    }

    [Fact]
    public async Task The_exact_server_source_can_be_read_without_enumerating_the_recipient_capture_history()
    {
        var f = new FormSuggestionAssistantFixture();
        var result = await f.Source.ExecuteAsync(new(f.TurnId, f.Context with { SourceRecordId = f.Runtime.SourceId },
            new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 1), "{}", []), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(f.Runtime.SourceId, Assert.IsType<FormSuggestionAssistantCandidatesPayload>(result.Value.Payload).SourceRecordId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("source")]
    [InlineData("subject")]
    public async Task An_argument_or_historical_offer_cannot_replace_the_exact_server_source(string kind)
    {
        var f = new FormSuggestionAssistantFixture();
        var context = kind switch
        {
            "missing" => f.Context with { SourceRecordId = null },
            "source" => f.Context with { SourceRecordId = Guid.NewGuid() },
            _ => f.Context with { SubjectUserId = Guid.NewGuid() },
        };
        var result = await f.Source.ExecuteAsync(new(f.TurnId, context, new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 1),
            "{}", [f.Offers()]), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure); Assert.Equal(kind == "source" ? 1 : 0, f.Runtime.Responses.Reads);
    }

    [Fact]
    public async Task A_source_record_argument_cannot_override_the_server_context()
    {
        var f = new FormSuggestionAssistantFixture();
        var result = await f.Source.ExecuteAsync(new(f.TurnId, f.Context, new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 1),
            JsonSerializer.Serialize(new { sourceRecordId = Guid.NewGuid() }), []), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(0, f.Runtime.Responses.Reads);
    }

    [Fact]
    public async Task Proposal_derives_all_revisions_and_ids_from_one_prior_read_without_requerying()
    {
        var f = new FormSuggestionAssistantFixture();
        var result = await f.Source.ExecuteAsync(new(f.TurnId, f.Context, new(BusinessFormSuggestionAssistantCapabilitySource.RegistrationId, 1),
            JsonSerializer.Serialize(new { sourceRecordId = f.Runtime.SourceId, suggestionId = f.Runtime.Suggestion.Id, values = Array.Empty<FormAssistantValue>(), reason = "Pidió seguimiento." }),
            [f.Offers(), f.Read()]), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var proposal = Assert.IsType<FormSuggestionAssistantRegistrationPayload>(result.Value.Payload);
        Assert.Equal(2, proposal.ReadOrdinal); Assert.Equal(f.Runtime.Set.Id, proposal.SuggestionSetId);
        Assert.Equal(f.Runtime.Set.Revision, proposal.ExpectedSuggestionRevision);
        Assert.Equal(f.Runtime.Assistant.Rows[0].DefinitionRevision, proposal.ExpectedDefinitionRevision);
        Assert.Single(proposal.Values); Assert.Equal(0, f.Runtime.Responses.Reads);
    }

    [Fact]
    public async Task Proposal_without_the_form_read_cannot_borrow_the_channel_read_as_authority()
    {
        var f = new FormSuggestionAssistantFixture();
        var result = await f.Source.ExecuteAsync(new(f.TurnId, f.Context, new(BusinessFormSuggestionAssistantCapabilitySource.RegistrationId, 1),
            JsonSerializer.Serialize(new { sourceRecordId = f.Runtime.SourceId, suggestionId = f.Runtime.Suggestion.Id, values = Array.Empty<FormAssistantValue>(), reason = "Pidió seguimiento." }),
            [f.Offers()]), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task A_model_cannot_replace_a_configured_mapped_value_with_another_allowed_choice()
    {
        var f = new FormSuggestionAssistantFixture();
        var other = BusinessFormCatalogItem.Create(f.Runtime.Assistant.Catalog.Catalog.Id, "B", "Bea", "Bea", null, null, 1);
        f.Runtime.Assistant.Catalog.Items.Add(other);
        var result = await f.Source.ExecuteAsync(new(f.TurnId, f.Context, new(BusinessFormSuggestionAssistantCapabilitySource.RegistrationId, 1),
            JsonSerializer.Serialize(new { sourceRecordId = f.Runtime.SourceId, suggestionId = f.Runtime.Suggestion.Id,
                values = new[] { new FormAssistantValue(f.Runtime.Assistant.Catalog.FieldId, "choice", other.Id.ToString("D")) }, reason = "Pidió seguimiento." }),
            [f.Offers(), f.Read()]), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Exact_typed_output_roundtrips_and_another_capability_version_is_rejected()
    {
        var f = new FormSuggestionAssistantFixture(); var payload = f.ReadPayload();
        var json = JsonSerializer.Serialize(payload, JsonSerializerOptions.Web);
        var result = f.Source.ParseOutput(new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 1), json);
        Assert.True(result.IsSuccess); Assert.IsType<FormSuggestionAssistantCandidatesPayload>(result.Value);
        Assert.True(f.Source.ParseOutput(new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 2), json).IsFailure);
    }

    [Fact]
    public async Task Authentic_assistant_suggestion_prepares_the_observed_values_and_real_lineage()
    {
        var f = new FormSuggestionAssistantFixture();
        var result = await f.Runtime.Policy.PrepareAsync(f.Runtime.Request with { Proposal = f.Proposal() }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.Single(result.Value.ProposedFields);
        Assert.Equal(f.Runtime.SourceId, result.Value.OriginSuggestion.OriginRecordId);
        Assert.Equal(f.Runtime.Suggestion.Id, result.Value.OriginSuggestion.OriginSuggestionId);
    }

    [Fact]
    public async Task A_target_revised_after_the_assistant_read_requires_a_new_proposal()
    {
        var f = new FormSuggestionAssistantFixture(); var proposal = f.Proposal();
        f.Runtime.Assistant.Rows[0] = f.Runtime.Assistant.Rows[0] with { DefinitionRevision = 4 };
        var result = await f.Runtime.Policy.PrepareAsync(f.Runtime.Request with { Proposal = proposal }, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure); Assert.Equal(FormSuggestionErrors.RevisionConflict, result.Error);
    }
}
