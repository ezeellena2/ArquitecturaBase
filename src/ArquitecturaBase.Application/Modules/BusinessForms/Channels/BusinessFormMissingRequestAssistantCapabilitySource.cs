using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

internal sealed class BusinessFormMissingRequestAssistantCapabilitySource(FormAssistantCandidatePolicy candidates) : IAssistantCapabilitySource
{
    public const string ProposeId = "forms.missing-request.propose";
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } = [new(ProposeId, 1, AssistantCapabilityKind.Proposal,
        "Record a clear intent to register something only when no accessible observed form matches. Reuse an observed generalized topic or propose a short generalized name and phrase in the recipient culture. Never include names, contact details, conversation quotations or response values. A quota limit or declined known form is not a missing form.",
        FormMissingRequestAssistantCodec.ArgumentsSchema, FormMissingRequestAssistantCodec.ProposalSchema,
        new([], []), new(2048, 2048, 1, 5))];
    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.TurnId == Guid.Empty || request.Context.SubjectUserId is not { } recipient
            || request.Capability is not { Id: ProposeId, Version: 1 }) return FormMissingRequestErrors.ContentInvalid;
        if (!await candidates.IsActiveRecipientAsync(recipient, request.Context.Culture, ct)) return FormDefinitionErrors.Forbidden;
        var arguments = FormMissingRequestAssistantCodec.ParseArguments(request.ArgumentsJson);
        if (arguments.IsFailure) return arguments.Error;
        var observed = FormMissingRequestAssistantCodec.Find(request.ObservedReads, request.TurnId, recipient, request.Context.Culture);
        if (observed.IsFailure) return observed.Error;
        if (arguments.Value.ExistingTopicId is { } id && !observed.Value.Snapshot.Topics.Any(topic => topic.Id == id)) return FormMissingRequestErrors.ContentInvalid;
        var payload = new MissingFormRequestProposalPayload("proposed", observed.Value.ReadOrdinal, arguments.Value.ExistingTopicId,
            arguments.Value.Name?.Trim(), arguments.Value.Description?.Trim());
        return new AssistantCapabilityResponse("proposed", payload, [], 1);
    }
    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) => FormMissingRequestAssistantCodec.ParseOutput(capability, outputJson);
}
