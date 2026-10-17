using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

// Only routes a controlled operation through the real WhatsApp owner. It does not qualify a model or BusinessForms.
internal sealed class WhatsAppAssistantOperationsDouble : IAssistantOperations
{
    private readonly Dictionary<Guid, AssistantOperationResponse> _responses = [];
    public List<AssistantChannelContext> ReadContexts { get; } = [];
    public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken ct) => Task.FromResult(new AssistantAvailabilityResponse("available"));
    public Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request, CancellationToken ct) =>
        throw new InvalidOperationException("The HTTP scenario advances the real dispatcher explicitly; it never submits a provider operation.");
    public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context, Guid requestId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); ReadContexts.Add(context);
        return Task.FromResult(Result.Success(_responses.GetValueOrDefault(requestId) ?? new(requestId, "pending", null, null, null)));
    }
    public void Complete(AssistantOperationResponse response) => _responses[response.RequestId] = response;
}

internal sealed record WhatsAppAssistantTestPayload : AssistantCapabilityPayload;

// A neutral owner snapshot is intentional: module integration tests remain removable without BusinessForms.
internal sealed class WhatsAppAssistantFormSource : IStructuredFormSource
{
    private readonly Dictionary<Guid, (Guid Recipient, AssistantObservedProposal Proposal)> _proposals = [];
    public Guid FieldId { get; } = Guid.NewGuid();
    public bool IsPublished { get; set; } = true;
    public StructuredFormSelectionRow Form { get; } = new(Guid.NewGuid(), Guid.NewGuid(), 1, "VISIT",
        new Dictionary<string,string> { ["es"] = "Registrar visita", ["en"] = "Register visit" }, 1,
        new Dictionary<string,string> { ["es"] = "Enviar", ["en"] = "Submit" }) { DefinitionRevision = 1 };
    public StructuredFormCheckRequest Snapshot => new(1, "es", Form.Name, Form.SubmitLabel,
        [new(FieldId, "field_" + FieldId.ToString("N"), "shortText", new Dictionary<string,string> { ["es"] = "Lugar", ["en"] = "Place" }, null, false, 80, null, null)], ["es", "en"]);
    public AssistantObservedProposal Proposal(Guid recipient)
    {
        var turn = Guid.NewGuid();
        var read = new AssistantObservedRead(turn, 1, new("forms.assistant-candidates", 1), new WhatsAppAssistantTestPayload());
        var proposal = new AssistantObservedProposal(turn, Guid.NewGuid(), new("forms.propose-registration", 1), new WhatsAppAssistantTestPayload()) { ObservedReads = [read] };
        _proposals.Add(proposal.Id, (recipient, proposal)); return proposal;
    }
    public Task<Result<StructuredFormPrefillResponse>> ValidatePrefillAsync(StructuredFormPrefillRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsPublished || !_proposals.TryGetValue(request.Proposal.Id, out var registered) || registered.Recipient != request.RecipientUserId
            || !ReferenceEquals(registered.Proposal, request.Proposal)) return Task.FromResult(Result.Failure<StructuredFormPrefillResponse>(SystemQueryErrors.NotFound));
        var field = new StructuredFormPrefillField(FieldId, "field_" + FieldId.ToString("N"), "shortText", "Lugar", "Sucursal Norte", "Sucursal Norte");
        return Task.FromResult(Result.Success(new StructuredFormPrefillResponse(Form, 1, [field], [], "La persona contó una visita y mencionó el lugar.", "es")));
    }
    public Task<StructuredFormPreparationResponse> PrepareAsync(Guid recipientUserId, string recipientCulture, Guid definitionId, Guid versionId, bool forSending, CancellationToken ct) => Task.FromResult(new StructuredFormPreparationResponse([], []));
    public Task LockSelectionAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<StructuredFormSelectionRow?> FindAsync(Guid recipientUserId, string recipientCulture, Guid definitionId, Guid versionId, CancellationToken ct) =>
        Task.FromResult(IsPublished && definitionId == Form.DefinitionId && versionId == Form.VersionId ? Form : null);
    public Task<StructuredFormSelectionRow?> FindAcceptedSessionAsync(Guid recipientUserId, string recipientCulture, Guid definitionId, Guid versionId, CancellationToken ct) =>
        Task.FromResult<StructuredFormSelectionRow?>(definitionId == Form.DefinitionId && versionId == Form.VersionId ? Form : null);
    public Task<PagedResult<StructuredFormSelectionRow>> ListAsync(Guid recipientUserId, string recipientCulture, ListStructuredFormsRequest request, CancellationToken ct) =>
        Task.FromResult(new PagedResult<StructuredFormSelectionRow>(IsPublished ? [Form] : [], request.Page, request.PageSize, IsPublished ? 1 : 0));
    public Task<Result<StructuredFormSuggestionsResponse>> ListSuggestionsAsync(Guid recipientUserId, string recipientCulture, Guid sourceRecordId, CancellationToken ct) => Task.FromResult(Result.Failure<StructuredFormSuggestionsResponse>(SystemQueryErrors.NotFound));
    public Task<Result<StructuredFormSuggestionPreparationResponse>> PrepareSuggestionAsync(StructuredFormSuggestionPreparationRequest request, CancellationToken ct) => Task.FromResult(Result.Failure<StructuredFormSuggestionPreparationResponse>(SystemQueryErrors.NotFound));
    public Task<Result<StructuredFormClarificationResponse>> ValidateClarificationAsync(StructuredFormClarificationRequest request, CancellationToken ct) => Task.FromResult(Result.Failure<StructuredFormClarificationResponse>(SystemQueryErrors.NotFound));
}
