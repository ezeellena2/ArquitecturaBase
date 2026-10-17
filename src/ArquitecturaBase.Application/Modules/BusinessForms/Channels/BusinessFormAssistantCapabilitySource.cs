using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

internal sealed class BusinessFormAssistantCapabilitySource(FormAssistantCandidatePolicy candidates) : IAssistantCapabilitySource
{
    public const string CandidatesId = "forms.assistant-candidates";
    public const string ClarificationId = "forms.clarify-registration";
    public const string RegistrationId = "forms.propose-registration";
    public const int Version = 1;
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } = [
        new(CandidatesId, Version, AssistantCapabilityKind.Read, "Read the complete published forms that this recipient may submit, their offering instructions and current scalar choices. Never infer absence from a failed read.",
            "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}", FormAssistantCapabilityCodec.ReadSchemaJson,
            new([], []), new(1024, 16384, 100, 5)),
        new(RegistrationId, Version, AssistantCapabilityKind.Proposal, "Propose one observed form and scalar values supported by the conversation. Never submit or confirm a response. Use the latest complete candidates read from this turn.",
            FormAssistantCapabilityCodec.InputSchemaJson, FormAssistantCapabilityCodec.ProposalSchemaJson,
            new([], [BusinessFormsPermissions.RecordsCreate]), new(16384, 16384, 40, 5)),
        new(ClarificationId, Version, AssistantCapabilityKind.Proposal, "Ask which form to complete only when two or three authorized observed forms plausibly match. Choose references from the latest complete candidates read in this turn. The owner supplies the fixed question and labels.",
            FormAssistantCapabilityCodec.ClarificationInputSchemaJson, FormAssistantCapabilityCodec.ClarificationOutputSchemaJson,
            new([], [BusinessFormsPermissions.RecordsCreate]), new(1024, 4096, 3, 5))];

    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.TurnId == Guid.Empty || request.Context.SubjectUserId is not { } recipient || recipient == Guid.Empty
            || request.Capability.Version != Version) return FormRecordErrors.InvalidResponse;
        if (request.Capability.Id == CandidatesId)
        {
            if (!FormAssistantCapabilityCodec.EmptyArguments(request.ArgumentsJson)) return FormRecordErrors.InvalidResponse;
            var result = await candidates.ListAsync(recipient, request.Context.Culture, ct);
            if (result.IsFailure) return result.Error;
            var forms = result.Value.Select(form => Candidate(form, request.Context.Culture)).ToArray();
            var payload = new FormAssistantCandidatesPayload("complete", recipient, request.Context.Culture, true, forms);
            if (!FormAssistantCapabilityCodec.ValidRead(payload)) return FormRecordErrors.InvalidResponse;
            return Response(payload, forms.Length);
        }
        if (request.Capability.Id is not (RegistrationId or ClarificationId)) return FormRecordErrors.InvalidResponse;
        if (!await candidates.IsEligibleAsync(recipient, request.Context.Culture, ct)) return FormRecordErrors.AuthorizationChanged;
        if (request.Capability.Id == ClarificationId) return Clarify(request, recipient);
        var arguments = FormAssistantCapabilityCodec.ParseArguments(request.ArgumentsJson);
        if (arguments.IsFailure) return arguments.Error;
        var observed = FormAssistantObservationPolicy.Find(request.ObservedReads, request.TurnId, recipient, request.Context.Culture,
            arguments.Value.DefinitionId, arguments.Value.VersionId);
        if (observed.IsFailure) return observed.Error;
        var values = FormAssistantObservationPolicy.Normalize(observed.Value.Form, arguments.Value.Values, request.Context.Culture);
        if (values.IsFailure) return values.Error;
        return Response(new FormAssistantRegistrationPayload("proposed", observed.Value.ReadOrdinal, observed.Value.Form.DefinitionId,
            observed.Value.Form.VersionId, observed.Value.Form.DefinitionRevision, values.Value, arguments.Value.Reason), values.Value.Count);
    }
    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
        FormAssistantCapabilityCodec.ParseOutput(capability, outputJson);

    private static Result<AssistantCapabilityResponse> Clarify(AssistantCapabilityRequest request, Guid recipient)
    {
        var arguments = FormAssistantCapabilityCodec.ParseClarificationArguments(request.ArgumentsJson);
        if (arguments.IsFailure) return arguments.Error;
        var forms = new List<FormAssistantClarificationReference>();
        var ordinal = 0;
        foreach (var selection in arguments.Value.Forms)
        {
            var observed = FormAssistantObservationPolicy.Find(request.ObservedReads, request.TurnId, recipient, request.Context.Culture,
                selection.DefinitionId, selection.VersionId);
            if (observed.IsFailure) return observed.Error;
            if (ordinal != 0 && ordinal != observed.Value.ReadOrdinal) return FormRecordErrors.InvalidResponse;
            ordinal = observed.Value.ReadOrdinal;
            forms.Add(new(selection.DefinitionId, selection.VersionId, observed.Value.Form.DefinitionRevision));
        }
        return Response(new FormAssistantClarificationPayload("clarification", ordinal, forms), forms.Count);
    }

    private static FormAssistantCandidate Candidate(FormPreparedAssistantCandidate form, string requested)
    {
        var culture = FormAssistantCandidatePolicy.PresentationCulture(form, requested);
        var questions = form.Questions.Select(question =>
        {
            var catalog = form.Catalogs.SingleOrDefault(catalog => catalog.FieldId == question.FieldId);
            var choices = catalog is not null
                ? catalog.Options.Select(option => new FormAssistantChoice(option.Id, option.Translations is { } texts && texts.TryGetValue(culture, out var text) ? text.Text : option.Text)).ToArray()
                : question.Options?.Select(option => new FormAssistantChoice(option.ChoiceId, FormAssistantCandidatePolicy.Text(option.Label, culture, form.OriginalCulture))).ToArray() ?? [];
            return new FormAssistantQuestion(question.FieldId, question.Type, FormAssistantCandidatePolicy.Text(question.Label, culture, form.OriginalCulture),
                question.MaxLength, question.SourceCatalogId, catalog?.Revision, choices);
        }).ToArray();
        return new(form.Form.DefinitionId, form.Form.VersionId, form.DefinitionRevision,
            FormAssistantCandidatePolicy.Text(form.Form.Name, culture, form.OriginalCulture), form.OfferWhen, form.OriginalCulture, questions);
    }
    private static Result<AssistantCapabilityResponse> Response(AssistantCapabilityPayload payload, int count) =>
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(payload, payload.GetType(), FormDraftPolicy.JsonOptions)) <= 16384
            ? new AssistantCapabilityResponse(payload is FormAssistantCandidatesPayload ? "complete" : payload is FormAssistantClarificationPayload ? "clarification" : "proposed", payload, [], count)
            : FormRecordErrors.InvalidResponse;
}
