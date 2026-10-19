using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

internal sealed class BusinessFormSuggestionAssistantCapabilitySource(FormSuggestionCandidatePolicy candidates,
    FormAssistantCandidatePolicy eligibility, FormSuggestionHistoryAccessGuard history) : IAssistantCapabilitySource
{
    public const string CandidatesId = "forms.suggestion-candidates";
    public const string RegistrationId = "forms.propose-suggestion";
    public const int Version = 1;
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } = [
        new(CandidatesId, Version, AssistantCapabilityKind.Read, "Read configured Assistant suggestions only for the exact captured response fixed by the trusted server context. Return the complete eligible set, source scalar answers, mapping values and each suggestion's own offering instruction. Always suggestions are handled by the owner without the model.",
            FormSuggestionAssistantCapabilityCodec.ReadInputSchemaJson, FormSuggestionAssistantCapabilityCodec.ReadSchemaJson,
            new([], [BusinessFormsPermissions.RecordsCreate]), new(1024, 16384, 20, 5)),
        new(RegistrationId, Version, AssistantCapabilityKind.Proposal, "Propose one configured suggestion from the latest complete suggestion read in this turn. Keep every mapped value and add only scalar values supported by the source response or conversation. Never replace a mapped value, send or confirm a response. Write the reason entirely in the conversation language identified by the trusted channel context, even when the form labels use its original language.",
            FormSuggestionAssistantCapabilityCodec.InputSchemaJson, FormSuggestionAssistantCapabilityCodec.ProposalSchemaJson,
            new([], [BusinessFormsPermissions.RecordsCreate]), new(16384, 16384, 40, 5))];

    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.TurnId == Guid.Empty || request.Context.SubjectUserId is not { } recipient || recipient == Guid.Empty
            || request.Capability.Version != Version) return FormRecordErrors.InvalidResponse;
        Guid? historyActor = request.Context.ChannelId == "whatsapp-assistant" && request.Context.ActorUserId == recipient
            ? null : request.Context.ActorUserId;
        if (!await history.CanReadAsync(historyActor, ct)) return FormDefinitionErrors.Forbidden;
        if (request.Capability.Id == CandidatesId)
        {
            if (!FormAssistantCapabilityCodec.EmptyArguments(request.ArgumentsJson)
                || request.Context.SourceRecordId is not { } sourceRecordId || sourceRecordId == Guid.Empty) return FormRecordErrors.InvalidResponse;
            var result = await candidates.LoadAsync(recipient, request.Context.Culture, sourceRecordId, ct);
            if (!await history.CanReadAsync(historyActor, ct)) return FormDefinitionErrors.Forbidden;
            if (result.IsFailure) return result.Error;
            var response = result.Value.Response;
            var rows = response.Candidates.Where(row => row.Mode == "Assistant").Select(row =>
            {
                var form = BusinessFormAssistantCapabilitySource.Candidate(result.Value.Prepared[row.SuggestionId] with { OfferWhen = row.When! });
                return new FormSuggestionAssistantCandidate(row.SuggestionId, form,
                    row.Fields.Select(field => new FormAssistantValue(field.FieldId, field.Type, field.Value)).ToArray(), row.CreatesAutomaticLoop);
            }).ToArray();
            var payload = new FormSuggestionAssistantCandidatesPayload("complete", recipient, request.Context.Culture, true,
                response.SourceRecordId, response.SuggestionSetId, response.Revision, response.SourceName, response.SourceNumber,
                response.SourceValues.Select(value => new FormSuggestionAssistantSourceValue(value.FieldId, value.Type, value.Label, SourceValue(value.Value))).ToArray(), rows);
            if (!FormSuggestionAssistantCapabilityCodec.ValidRead(payload)) return FormRecordErrors.InvalidResponse;
            return Response(payload, rows.Length);
        }
        if (request.Capability.Id != RegistrationId) return FormRecordErrors.InvalidResponse;
        if (!await eligibility.IsEligibleAsync(recipient, request.Context.Culture, ct)) return FormRecordErrors.AuthorizationChanged;
        var input = FormSuggestionAssistantCapabilityCodec.ParseArguments(request.ArgumentsJson);
        if (input.IsFailure) return input.Error;
        if (request.Context.SourceRecordId != input.Value.SourceRecordId) return FormRecordErrors.InvalidResponse;
        var observed = FormSuggestionAssistantObservationPolicy.Find(request.ObservedReads, request.TurnId, recipient, request.Context.Culture,
            input.Value.SourceRecordId, input.Value.SuggestionId);
        if (observed.IsFailure) return observed.Error;
        var values = FormSuggestionAssistantObservationPolicy.Normalize(observed.Value, input.Value.Values, request.Context.Culture);
        if (values.IsFailure) return values.Error;
        if (!await history.CanReadAsync(historyActor, ct)) return FormDefinitionErrors.Forbidden;
        var selected = observed.Value;
        return Response(new FormSuggestionAssistantRegistrationPayload("proposed", selected.ReadOrdinal, selected.Payload.SourceRecordId,
            selected.Payload.SuggestionSetId!.Value, selected.Payload.SuggestionRevision, selected.Suggestion.SuggestionId,
            selected.Suggestion.Form.DefinitionId, selected.Suggestion.Form.VersionId, selected.Suggestion.Form.DefinitionRevision,
            values.Value, input.Value.Reason), values.Value.Count);
    }

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
        FormSuggestionAssistantCapabilityCodec.ParseOutput(capability, outputJson);

    private static string? SourceValue(object? value) => value switch
    {
        null => null, StructuredRecordChoiceValue choice => choice.Label, bool boolean => boolean ? "true" : "false",
        decimal number => number.ToString(CultureInfo.InvariantCulture), string text => text,
        _ => throw new InvalidOperationException("A suggestion source value must be scalar.")
    };
    private static Result<AssistantCapabilityResponse> Response(AssistantCapabilityPayload payload, int count)
    {
        var read = payload is FormSuggestionAssistantCandidatesPayload;
        var state = read ? "complete" : "proposed";
        if (FormAssistantCapabilityCodec.ResponseBytes(payload, state, !read) > 16384)
            return read ? FormSuggestionErrors.ReadUnavailable : FormRecordErrors.InvalidResponse;
        return new AssistantCapabilityResponse(state, payload, [], count);
    }
}
