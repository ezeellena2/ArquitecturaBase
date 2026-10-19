using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormSuggestionAssistantBudgetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(16384, true)]
    [InlineData(16385, false)]
    public async Task The_complete_read_budget_includes_its_tool_envelope(int bytes, bool fits)
    {
        var (fixture, payload) = await ArrangeReadAsync(bytes, false);
        Assert.Equal(bytes, ReadBytes(payload));
        var result = await fixture.Source.ExecuteAsync(Read(fixture), Ct);
        Assert.Equal(fits, result.IsSuccess);
        if (fits)
        {
            Assert.Equal(FormDraftPolicy.Serialize(payload), FormDraftPolicy.Serialize(Assert.IsType<FormSuggestionAssistantCandidatesPayload>(result.Value.Payload)));
            Assert.True(payload.IsComplete);
        }
        else
        {
            Assert.Equal("Forms.Suggestion.ReadUnavailable", result.Error.Code);
            Assert.Equal(ErrorType.Failure, result.Error.Type);
        }
    }

    [Fact]
    public async Task A_read_that_only_fits_without_its_envelope_is_unavailable_instead_of_partial()
    {
        var (fixture, payload) = await ArrangeReadAsync(16374, true);
        Assert.Equal(16374, Encoding.UTF8.GetByteCount(FormDraftPolicy.Serialize(payload)));
        Assert.True(ReadBytes(payload) > 16384);
        Assert.True(FormSuggestionAssistantCapabilityCodec.ValidRead(payload));
        var result = await fixture.Source.ExecuteAsync(Read(fixture), Ct);
        Assert.True(result.IsFailure);
        Assert.Equal("Forms.Suggestion.ReadUnavailable", result.Error.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    [Theory]
    [InlineData(16384, true)]
    [InlineData(16385, false)]
    public async Task The_proposal_budget_includes_its_generated_proposal_identifier(int bytes, bool fits)
    {
        var fixture = new FormSuggestionAssistantFixture();
        var fields = TextFields();
        var read = fixture.ReadPayload();
        var suggestion = Assert.Single(read.Suggestions);
        var form = suggestion.Form with { Questions = [.. suggestion.Form.Questions,
            .. fields.Select(field => new FormAssistantQuestion(field.FieldId, field.Type, field.Label["es"], field.MaxLength, null, null, []))] };
        read = read with { Suggestions = [suggestion with { Form = form }] };
        var values = fields.Select(field => new FormAssistantValue(field.FieldId, field.Type, "x")).ToArray();
        AssistantCapabilityRequest Request() => new(fixture.TurnId, fixture.Context,
            new(BusinessFormSuggestionAssistantCapabilitySource.RegistrationId, 1),
            FormDraftPolicy.Serialize(new { sourceRecordId = fixture.Runtime.SourceId, suggestionId = fixture.Runtime.Suggestion.Id, values, reason = "Requested follow-up." }),
            [fixture.Read() with { Payload = read }]);
        var baseline = await fixture.Source.ExecuteAsync(Request(), Ct);
        Assert.True(baseline.IsSuccess);
        var payload = Assert.IsType<FormSuggestionAssistantRegistrationPayload>(baseline.Value.Payload);
        var extra = bytes - ProposalBytes(payload);
        Assert.True(extra > 0);
        values = values.Select((value, index) => value with { Value = new string('x', 1 + extra / 2 + (index == 0 ? extra % 2 : 0)) }).ToArray();
        payload = payload with { Values = payload.Values.Select(value => values.SingleOrDefault(other => other.FieldId == value.FieldId) ?? value).ToArray() };
        Assert.All(values, value => Assert.InRange(value.Value.Length, 1, 10000));
        Assert.Equal(bytes, ProposalBytes(payload));
        Assert.True(FormSuggestionAssistantCapabilityCodec.ValidProposal(payload));
        var request = Request();
        Assert.InRange(Encoding.UTF8.GetByteCount(request.ArgumentsJson), 1, 16384);

        var result = await fixture.Source.ExecuteAsync(request, Ct);

        Assert.Equal(fits, result.IsSuccess);
        if (fits) Assert.Equal(FormDraftPolicy.Serialize(payload), FormDraftPolicy.Serialize(Assert.IsType<FormSuggestionAssistantRegistrationPayload>(result.Value.Payload)));
        else
        {
            Assert.Equal("Forms.Record.InvalidResponse", result.Error.Code);
            Assert.Equal(ErrorType.Validation, result.Error.Type);
        }
    }

    private static async Task<(FormSuggestionAssistantFixture Fixture, FormSuggestionAssistantCandidatesPayload Payload)> ArrangeReadAsync(int bytes, bool payloadOnly)
    {
        var fixture = new FormSuggestionAssistantFixture();
        var source = fixture.Runtime.Responses.ExportRows[0];
        var schema = FormDraftPolicy.Read<FormVersionSchema>(source.Record.SchemaJson);
        var fields = TextFields();
        var schemaJson = FormDraftPolicy.Serialize(schema with { Questions = [.. schema.Questions, .. fields] });
        var values = JsonSerializer.Deserialize<JsonElement[]>(source.Record.ValuesJson, JsonSerializerOptions.Web)!;
        void Store(string first, string second)
        {
            var added = fields.Select((field, index) => JsonSerializer.SerializeToElement(new { fieldId = field.FieldId, value = index == 0 ? first : second }, JsonSerializerOptions.Web));
            fixture.Runtime.Responses.ExportRows = [source with
            {
                Record = source.Record with { SchemaJson = schemaJson, ValuesJson = FormDraftPolicy.Serialize(values.Concat(added).ToArray()) },
                Version = source.Version! with { SchemaJson = schemaJson }
            }];
        }
        Store("x", "x");
        var baseline = await fixture.Source.ExecuteAsync(Read(fixture), Ct);
        Assert.True(baseline.IsSuccess);
        var payload = Assert.IsType<FormSuggestionAssistantCandidatesPayload>(baseline.Value.Payload);
        var extra = bytes - (payloadOnly ? Encoding.UTF8.GetByteCount(FormDraftPolicy.Serialize(payload)) : ReadBytes(payload));
        Assert.True(extra > 0);
        var first = new string('x', 1 + extra / 2 + extra % 2);
        var second = new string('x', 1 + extra / 2);
        Assert.InRange(first.Length, 1, 10000);
        Assert.InRange(second.Length, 1, 10000);
        Store(first, second);
        payload = payload with { SourceValues = payload.SourceValues.Select(value => value.FieldId == fields[0].FieldId ? value with { Value = first }
            : value.FieldId == fields[1].FieldId ? value with { Value = second } : value).ToArray() };
        Assert.True(FormSuggestionAssistantCapabilityCodec.ValidRead(payload));
        return (fixture, payload);
    }

    private static CanonicalFormField[] TextFields() => Enumerable.Range(0, 2).Select(index =>
    {
        var id = Guid.NewGuid();
        return new CanonicalFormField(id, "field_" + id.ToString("N"), "longText",
            new Dictionary<string, string> { ["es"] = "Notes " + index, ["en"] = "Notes " + index }, null, false, 10000, null);
    }).ToArray();

    private static AssistantCapabilityRequest Read(FormSuggestionAssistantFixture fixture) => new(fixture.TurnId, fixture.Context,
        new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 1), "{}", []);
    private static int ReadBytes(FormSuggestionAssistantCandidatesPayload payload) =>
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { state = "complete", payload }, JsonSerializerOptions.Web));
    private static int ProposalBytes(FormSuggestionAssistantRegistrationPayload payload) =>
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { state = "proposed", proposalId = Guid.Empty, payload }, JsonSerializerOptions.Web));
}
