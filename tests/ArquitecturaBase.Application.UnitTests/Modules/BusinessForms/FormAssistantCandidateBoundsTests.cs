using System.Globalization;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormAssistantCandidateBoundsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_inaccessible_form_does_not_remove_the_other_accessible_candidate()
    {
        var f = new FormAssistantFixture();
        var row = f.Rows[0];
        f.Catalog.Items.Clear();
        f.Rows.Add(row with { DefinitionId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(),
            SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [])), QuestionCount = 0 });
        var payload = await ReadAsync(f);
        Assert.True(payload.IsComplete);
        Assert.Equal(f.Rows[1].DefinitionId, Assert.Single(payload.Forms).DefinitionId);
    }

    [Fact]
    public async Task More_than_one_hundred_forms_keep_observed_candidates_without_claiming_complete_absence()
    {
        var f = new FormAssistantFixture(); var row = f.Rows[0];
        f.Rows.Clear();
        for (var i = 0; i < 101; i++) f.Rows.Add(row with { DefinitionId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(),
            SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [])), QuestionCount = 0, OfferWhen = "Visita" });
        var payload = await ReadAsync(f);
        Assert.False(payload.IsComplete);
        Assert.InRange(payload.Forms.Count, 1, 100);
        AssertBounded(payload);
        var selected = payload.Forms[0]; var turn = Guid.CreateVersion7();
        var proposal = await f.Source.ExecuteAsync(new(turn, f.Context, new(BusinessFormAssistantCapabilitySource.RegistrationId, 1),
            FormDraftPolicy.Serialize(new { selected.DefinitionId, selected.VersionId, values = Array.Empty<FormAssistantValue>(), reason = "Contó una visita." }),
            [new(turn, 1, new(BusinessFormAssistantCapabilitySource.CandidatesId, 1), payload)]), Ct);
        Assert.True(proposal.IsSuccess);
        Assert.Equal(selected.DefinitionId, Assert.IsType<FormAssistantRegistrationPayload>(proposal.Value.Payload).DefinitionId);
    }

    [Fact]
    public async Task Rich_forms_and_topics_are_bounded_by_the_complete_tool_envelope()
    {
        var f = new FormAssistantFixture(); var row = f.Rows[0];
        f.Rows.Clear();
        for (var i = 0; i < 60; i++) f.Rows.Add(row with { DefinitionId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(),
            OfferWhen = new string('á', 1000) });
        for (var i = 0; i < 101; i++) f.Topics.Add(new(Guid.CreateVersion7(), "Tema " + i, new string('á', 500), "es"));
        var payload = await ReadAsync(f);
        Assert.False(payload.IsComplete);
        Assert.NotEmpty(payload.Forms);
        AssertBounded(payload);
        Assert.True(f.Source.ParseOutput(new(BusinessFormAssistantCapabilitySource.CandidatesId, 1), FormDraftPolicy.Serialize(payload)).IsSuccess);
    }

    [Fact]
    public async Task A_single_oversized_candidate_does_not_hide_a_later_small_candidate()
    {
        var f = new FormAssistantFixture(); var row = f.Rows[0];
        var schema = FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson);
        for (var i = 0; i < 12; i++) f.Catalog.Items.Add(BusinessFormCatalogItem.Create(f.Catalog.Catalog.Id, "P" + i, "Persona " + i, "Person " + i, null, null, i + 1));
        var fields = Enumerable.Range(0, 40).Select(_ => schema.Questions[0] with { FieldId = Guid.CreateVersion7(),
            Key = "field_" + Guid.CreateVersion7().ToString("N") }).ToArray();
        f.Rows[0] = row with { SchemaJson = FormDraftPolicy.Serialize(schema with { Questions = fields }), QuestionCount = fields.Length };
        f.Rows.Add(row with { DefinitionId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(),
            SchemaJson = FormDraftPolicy.Serialize(schema with { Questions = [] }), QuestionCount = 0 });
        var payload = await ReadAsync(f);
        Assert.False(payload.IsComplete);
        Assert.Equal(f.Rows[1].DefinitionId, Assert.Single(payload.Forms).DefinitionId);
        AssertBounded(payload);
    }

    [Theory]
    [InlineData("label", "😀")]
    [InlineData("label", "👩‍💻")]
    [InlineData("choice", "e\u0301")]
    [InlineData("choice", "👩‍💻")]
    public async Task Question_and_choice_texts_at_the_editors_grapheme_limit_survive_discovery_and_the_codec(string target, string grapheme)
    {
        var f = new FormAssistantFixture();
        var text = string.Concat(Enumerable.Repeat(grapheme, 160));
        Assert.Equal(160, StringInfo.ParseCombiningCharacters(text).Length);
        Assert.True(text.Length > 160);
        f.Rows[0] = ManualChoice(f.Rows[0], target == "label" ? text : "Pregunta", target == "choice" ? text : "Opción");

        var payload = await ReadAsync(f);

        Assert.True(payload.IsComplete);
        var question = Assert.Single(Assert.Single(payload.Forms).Questions);
        Assert.Equal(target == "label" ? text : "Pregunta", question.Label);
        Assert.Equal(target == "choice" ? text : "Opción", Assert.Single(question.Choices).Text);
        Assert.True(f.Source.ParseOutput(new(BusinessFormAssistantCapabilitySource.CandidatesId, 1), FormDraftPolicy.Serialize(payload)).IsSuccess);
    }

    [Theory]
    [InlineData("label")]
    [InlineData("choice")]
    [InlineData("name")]
    [InlineData("offerWhen")]
    public async Task An_invalid_candidate_is_excluded_without_hiding_a_later_candidate_or_claiming_complete_absence(string target)
    {
        var f = new FormAssistantFixture();
        var valid = ManualChoice(f.Rows[0], "Pregunta", "Opción");
        var excessive = string.Concat(Enumerable.Repeat("👩‍💻", 161));
        f.Rows[0] = target switch
        {
            "label" => ManualChoice(valid, excessive, "Opción"),
            "choice" => ManualChoice(valid, "Pregunta", excessive),
            "name" => valid with { NameJson = FormDraftPolicy.Serialize(Texts(string.Concat(Enumerable.Repeat("😀", 61)))) },
            _ => valid with { OfferWhen = string.Concat(Enumerable.Repeat("😀", 501)) }
        };
        f.Rows.Add(valid with { DefinitionId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7() });

        var payload = await ReadAsync(f);

        Assert.False(payload.IsComplete);
        Assert.Equal(f.Rows[1].DefinitionId, Assert.Single(payload.Forms).DefinitionId);
        AssertBounded(payload);
        Assert.True(f.Source.ParseOutput(new(BusinessFormAssistantCapabilitySource.CandidatesId, 1), FormDraftPolicy.Serialize(payload)).IsSuccess);
    }

    [Theory]
    [InlineData("name", 60)]
    [InlineData("offerWhen", 500)]
    public async Task Name_and_offer_instructions_keep_their_existing_UTF16_limits(string target, int graphemes)
    {
        var f = new FormAssistantFixture();
        var text = string.Concat(Enumerable.Repeat("😀", graphemes));
        var row = ManualChoice(f.Rows[0], "Pregunta", "Opción");
        f.Rows[0] = target == "name" ? row with { NameJson = FormDraftPolicy.Serialize(Texts(text)) } : row with { OfferWhen = text };

        var payload = await ReadAsync(f);

        Assert.True(payload.IsComplete);
        var candidate = Assert.Single(payload.Forms);
        Assert.Equal(text, target == "name" ? candidate.Name : candidate.OfferWhen);
        Assert.True(f.Source.ParseOutput(new(BusinessFormAssistantCapabilitySource.CandidatesId, 1), FormDraftPolicy.Serialize(payload)).IsSuccess);
    }

    [Fact]
    public async Task A_payload_that_fits_without_its_envelope_is_still_marked_partial()
    {
        var f = new FormAssistantFixture();
        var baseline = await ReadAsync(f);
        for (var i = 0; i < 30; i++) f.Topics.Add(new(Guid.CreateVersion7(), "Tema", "x", "es"));
        var baseBytes = Encoding.UTF8.GetByteCount(FormDraftPolicy.Serialize(baseline with { Topics = f.Topics }));
        var extra = 16374 - baseBytes;
        for (var i = 0; i < f.Topics.Count; i++) f.Topics[i] = f.Topics[i] with
            { Description = new string('x', 1 + extra / f.Topics.Count + (i < extra % f.Topics.Count ? 1 : 0)) };
        var full = baseline with { Topics = f.Topics };
        Assert.All(f.Topics, topic => Assert.InRange(topic.Description.Length, 1, 500));
        Assert.Equal(16374, Encoding.UTF8.GetByteCount(FormDraftPolicy.Serialize(full)));
        Assert.True(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { state = full.State, payload = full }, JsonSerializerOptions.Web)) > 16384);
        var result = await ReadAsync(f);
        Assert.False(result.IsComplete);
        AssertBounded(result);
    }

    private static async Task<FormAssistantCandidatesPayload> ReadAsync(FormAssistantFixture f)
    {
        var result = await f.Source.ExecuteAsync(new(Guid.CreateVersion7(), f.Context,
            new(BusinessFormAssistantCapabilitySource.CandidatesId, 1), "{}", []), Ct);
        Assert.True(result.IsSuccess);
        return Assert.IsType<FormAssistantCandidatesPayload>(result.Value.Payload);
    }

    private static FormAssistantCandidateRow ManualChoice(FormAssistantCandidateRow row, string label, string choice)
    {
        var schema = FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson);
        var field = schema.Questions[0] with { Label = Texts(label), Source = null, Options = [new(Guid.CreateVersion7(), Texts(choice))] };
        return row with { SchemaJson = FormDraftPolicy.Serialize(schema with { Questions = [field] }), QuestionCount = 1 };
    }

    private static Dictionary<string, string> Texts(string text) => new Dictionary<string, string> { ["es"] = text, ["en"] = text };

    private static void AssertBounded(FormAssistantCandidatesPayload payload)
    {
        var json = JsonSerializer.Serialize(new { state = payload.State, payload }, JsonSerializerOptions.Web);
        Assert.InRange(Encoding.UTF8.GetByteCount(json), 1, 16384);
        Assert.Equal("partial", payload.State);
    }
}
