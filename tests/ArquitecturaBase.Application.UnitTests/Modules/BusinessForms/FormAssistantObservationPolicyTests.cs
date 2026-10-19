using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormAssistantObservationPolicyTests
{
    [Theory]
    [InlineData("decimal", "1.500,50")]
    [InlineData("date", "18/10/2026")]
    [InlineData("boolean", "sí")]
    [InlineData("shortText", "Text exceeds eight")]
    [InlineData("choice", "Norte")]
    [InlineData("choice", "")]
    [InlineData("choice", "019abcde100070008000000000000001")]
    public void One_invalid_scalar_does_not_discard_the_other_observed_values(string type, string invalid)
    {
        var validId = Guid.NewGuid();
        var invalidId = Guid.NewGuid();
        var form = Candidate([Question(validId, "shortText"), Question(invalidId, type)]);
        var result = FormAssistantObservationPolicy.Normalize(form,
            [new(validId, "shortText", "Norte"), new(invalidId, type, invalid)], "es");
        Assert.True(result.IsSuccess);
        Assert.Equal(new FormAssistantValue(validId, "shortText", "Norte"), Assert.Single(result.Value));
    }
    [Theory]
    [InlineData("decimal", "empty")]
    [InlineData("date", "empty")]
    [InlineData("shortText", "control")]
    [InlineData("shortText", "oversized")]
    public async Task The_capability_discards_one_invalid_input_value_before_publishing_its_strict_output(string type, string invalidKind)
    {
        var fixture = new FormAssistantFixture();
        var turn = Guid.NewGuid();
        var validId = Guid.NewGuid();
        var invalidId = Guid.NewGuid();
        var form = fixture.ReadPayload().Forms[0] with { Questions = [Question(validId, "shortText"), Question(invalidId, type)] };
        var invalid = invalidKind switch { "control" => "bad\0text", "oversized" => new string('a', 10001), _ => "" };
        var json = FormDraftPolicy.Serialize(new { form.DefinitionId, form.VersionId,
            values = new[] { new FormAssistantValue(validId, "shortText", "Norte"), new(invalidId, type, invalid) }, reason = "Visita." });
        var read = fixture.Read(turn) with { Payload = fixture.ReadPayload() with { Forms = [form] } };
        var result = await fixture.Source.ExecuteAsync(new AssistantCapabilityRequest(turn, fixture.Context,
            new(BusinessFormAssistantCapabilitySource.RegistrationId, 1), json, [read]), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<FormAssistantRegistrationPayload>(result.Value.Payload);
        Assert.Equal(new FormAssistantValue(validId, "shortText", "Norte"), Assert.Single(payload.Values));
        Assert.True(fixture.Source.ParseOutput(new(BusinessFormAssistantCapabilitySource.RegistrationId, 1),
            FormDraftPolicy.Serialize(payload)).IsSuccess);
        Assert.True(fixture.Source.ParseOutput(new(BusinessFormAssistantCapabilitySource.RegistrationId, 1),
            FormDraftPolicy.Serialize(payload with { Values = [.. payload.Values, new(invalidId, type, invalid)] })).IsFailure);
    }
    [Fact]
    public void An_unknown_field_still_cannot_borrow_the_observed_form()
    {
        var form = Candidate([Question(Guid.NewGuid(), "shortText")]);
        Assert.True(FormAssistantObservationPolicy.Normalize(form,
            [new(Guid.NewGuid(), "shortText", "Norte")], "es").IsFailure);
    }
    [Theory]
    [InlineData("Norte")]
    [InlineData("")]
    [InlineData("019abcde100070008000000000000001")]
    public async Task A_choice_without_a_canonical_GUID_is_omitted_before_the_strict_proposal_is_published(string invalid)
    {
        var fixture = new FormAssistantFixture();
        var turn = Guid.NewGuid();
        var validId = Guid.NewGuid();
        var invalidId = Guid.NewGuid();
        var form = fixture.ReadPayload().Forms[0] with { Questions = [Question(validId, "shortText"), Question(invalidId, "choice")] };
        var json = FormDraftPolicy.Serialize(new { form.DefinitionId, form.VersionId,
            values = new[] { new FormAssistantValue(validId, "shortText", "Norte"), new(invalidId, "choice", invalid) }, reason = "Visita." });
        var read = fixture.Read(turn) with { Payload = fixture.ReadPayload() with { Forms = [form] } };

        var result = await fixture.Source.ExecuteAsync(new AssistantCapabilityRequest(turn, fixture.Context,
            new(BusinessFormAssistantCapabilitySource.RegistrationId, 1), json, [read]), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<FormAssistantRegistrationPayload>(result.Value.Payload);
        Assert.Equal(new FormAssistantValue(validId, "shortText", "Norte"), Assert.Single(payload.Values));
        Assert.True(fixture.Source.ParseOutput(new(BusinessFormAssistantCapabilitySource.RegistrationId, 1),
            FormDraftPolicy.Serialize(payload)).IsSuccess);
    }
    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("019abcde-1000-7000-8000-000000000001")]
    public void A_canonical_but_unobserved_choice_GUID_still_rejects_the_whole_proposal(string unobserved)
    {
        var validId = Guid.NewGuid();
        var choiceId = Guid.NewGuid();
        var form = Candidate([Question(validId, "shortText"), Question(choiceId, "choice")]);
        Assert.True(FormAssistantObservationPolicy.Normalize(form,
            [new(validId, "shortText", "Norte"), new(choiceId, "choice", unobserved)], "es").IsFailure);
    }
    [Fact]
    public void A_mismatched_field_type_still_rejects_the_whole_proposal()
    {
        var fieldId = Guid.NewGuid();
        var form = Candidate([Question(fieldId, "shortText")]);
        Assert.True(FormAssistantObservationPolicy.Normalize(form, [new(fieldId, "boolean", "true")], "es").IsFailure);
    }
    [Fact]
    public void The_shared_value_schema_declares_the_canonical_scalar_formats()
    {
        using var schema = JsonDocument.Parse(FormAssistantCapabilityCodec.InputSchemaJson);
        var description = schema.RootElement.GetProperty("properties").GetProperty("values")
            .GetProperty("items").GetProperty("properties").GetProperty("value").GetProperty("description").GetString();
        Assert.Contains("1234.50", description, StringComparison.Ordinal);
        Assert.Contains("yyyy-MM-dd", description, StringComparison.Ordinal);
        Assert.Contains("true", description, StringComparison.Ordinal);
        Assert.Contains("false", description, StringComparison.Ordinal);
        Assert.Contains("GUID", description, StringComparison.Ordinal);
    }
    private static FormAssistantQuestion Question(Guid id, string type) =>
        new(id, type, "Pregunta", type == "shortText" ? 8 : null, null, null,
            type == "choice" ? [new(Guid.NewGuid(), "Opción observada")] : []);
    private static FormAssistantCandidate Candidate(IReadOnlyList<FormAssistantQuestion> questions) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, "Visita", "Cuando registra una visita", "es", questions);
}
