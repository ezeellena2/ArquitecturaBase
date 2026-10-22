using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormMultipleChoiceAssistantReadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_mixed_form_stays_available_with_its_scalar_questions_without_proposing_the_multiple_selection()
    {
        var fixture = new FormAssistantFixture();
        var row = fixture.Rows[0]; var schema = FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson);
        var multi = Multiple();
        fixture.Rows[0] = row with { SchemaJson = FormDraftPolicy.Serialize(schema with { Questions = [.. schema.Questions, multi] }), QuestionCount = row.QuestionCount + 1 };
        var result = await fixture.Candidates.ListAsync(fixture.UserId, "es", Ct);
        Assert.True(result.IsSuccess);
        var candidate = Assert.Single(result.Value.Forms);
        Assert.Equal(row.DefinitionId, candidate.Form.DefinitionId);
        Assert.Equal(fixture.Catalog.FieldId, Assert.Single(candidate.Questions).FieldId);
        Assert.DoesNotContain(candidate.Questions, question => question.FieldId == multi.FieldId);
        Assert.Empty(fixture.Catalog.Locks);
    }

    [Fact]
    public async Task Suggestion_discovery_keeps_the_scalar_context_and_candidate_but_omits_the_multiple_source_value()
    {
        var fixture = new FormSuggestionAssistantFixture();
        var captured = Assert.Single(fixture.Runtime.Responses.ExportRows);
        var schema = FormDraftPolicy.Read<FormVersionSchema>(captured.Record.SchemaJson); var multi = Multiple();
        using var document = JsonDocument.Parse(captured.Record.ValuesJson);
        var values = document.RootElement.EnumerateArray().Select(value => value.Clone()).ToList();
        values.Add(JsonSerializer.SerializeToElement(new { fieldId = multi.FieldId, value = multi.Options!.Select(option => option.ChoiceId).ToArray() }));
        var schemaJson = FormDraftPolicy.Serialize(schema with { Questions = [.. schema.Questions, multi] });
        fixture.Runtime.Responses.ExportRows = [captured with { Record = captured.Record with { SchemaJson = schemaJson, ValuesJson = JsonSerializer.Serialize(values) }, Version = captured.Version! with { SchemaJson = schemaJson } }];
        var result = await fixture.Source.ExecuteAsync(new(fixture.TurnId, fixture.Context,
            new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 1), "{}", [fixture.Offers()]), Ct);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<FormSuggestionAssistantCandidatesPayload>(result.Value.Payload);
        Assert.True(payload.IsComplete); Assert.Single(payload.Suggestions);
        Assert.Equal(fixture.Runtime.SourceFieldId, Assert.Single(payload.SourceValues).FieldId);
        Assert.DoesNotContain(payload.SourceValues, value => value.FieldId == multi.FieldId);
        Assert.Single(payload.Suggestions[0].MappedValues);
    }

    [Fact]
    public void Mappings_exclude_only_multiple_questions_without_disabling_the_scalar_mapping_contract()
    {
        var catalog = Guid.NewGuid();
        Assert.True(FormSuggestionPolicy.CanMap("choice", catalog, "choice", catalog));
        Assert.False(FormSuggestionPolicy.CanMap("multiChoice", catalog, "choice", catalog));
        Assert.False(FormSuggestionPolicy.CanMap("choice", catalog, "multiChoice", catalog));
        Assert.False(FormSuggestionPolicy.CanMap("multiChoice", catalog, "multiChoice", catalog));
    }

    private static CanonicalFormField Multiple() => new(Guid.NewGuid(), "risks", "multiChoice",
        new Dictionary<string, string> { ["es"] = "Riesgos", ["en"] = "Risks" }, null, false, null,
        [new(Guid.NewGuid(), new Dictionary<string, string> { ["es"] = "Frío", ["en"] = "Cold" }),
         new(Guid.NewGuid(), new Dictionary<string, string> { ["es"] = "Ruido", ["en"] = "Noise" })], Selection: new(0, 3));
}
