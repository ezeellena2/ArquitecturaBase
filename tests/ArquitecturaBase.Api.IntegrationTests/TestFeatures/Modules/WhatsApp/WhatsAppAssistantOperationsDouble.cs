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
    public List<string> Calls { get; } = [];
    public List<AssistantOperationRequest> SubmitRequests { get; } = [];
    public string AvailabilityState { get; set; } = "available";
    public bool AllowSubmit { get; set; }
    public Error? SubmissionFailure { get; set; }
    public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken ct) => Task.FromResult(new AssistantAvailabilityResponse(AvailabilityState));
    public Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!AllowSubmit) throw new InvalidOperationException("The HTTP scenario advances the real dispatcher explicitly; it never submits a provider operation.");
        Calls.Add("Submit"); SubmitRequests.Add(request);
        return Task.FromResult(SubmissionFailure is {} error ? Result.Failure<AssistantOperationAcceptedResponse>(error)
            : Result.Success(new AssistantOperationAcceptedResponse(request.RequestId, "pending")));
    }
    public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context, Guid requestId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Calls.Add("Get"); ReadContexts.Add(context);
        if (_responses.TryGetValue(requestId, out var completed)) return Task.FromResult(Result.Success(completed));
        return Task.FromResult(SubmitRequests.Any(request=>request.RequestId==requestId)
            ? Result.Success(new AssistantOperationResponse(requestId, "pending", null, null, null))
            : Result.Failure<AssistantOperationResponse>(SystemQueryErrors.NotFound));
    }
    public void Complete(AssistantOperationResponse response) => _responses[response.RequestId] = response;
}

internal sealed record WhatsAppAssistantTestPayload : AssistantCapabilityPayload;

// A neutral owner snapshot is intentional: module integration tests remain removable without BusinessForms.
internal sealed class WhatsAppAssistantFormSource : IStructuredFormSource
{
    private readonly Dictionary<Guid, (Guid Recipient, AssistantObservedProposal Proposal)> _proposals = [];
    public Guid FieldId { get; } = Guid.NewGuid();
    public Guid AlternativeFieldId { get; } = Guid.NewGuid();
    public Guid SuggestionSetId { get; } = Guid.NewGuid();
    public Guid SuggestionId { get; } = Guid.NewGuid();
    public string? SuggestionMode { get; set; }
    public bool SuggestionCreatesLoop { get; set; }
    public List<StructuredFormSuggestionPreparationRequest> SuggestionPreparations { get; } = [];
    public Func<Guid, CancellationToken, Task<bool>>? CanReadAsync { get; set; }
    public Func<Guid, Guid, CancellationToken, Task<bool>>? CanUseCapturedSourceAsync { get; set; }
    public bool IsPublished { get; set; } = true;
    public bool ValuesAvailableForSending { get; set; } = true;
    public Func<Guid, CancellationToken, Task<bool>>? CanCompleteAsync { get; set; }
    public StructuredFormSelectionRow Form { get; } = new(Guid.NewGuid(), Guid.NewGuid(), 1, "VISIT",
        new Dictionary<string,string> { ["es"] = "Registrar visita", ["en"] = "Register visit" }, 1,
        new Dictionary<string,string> { ["es"] = "Enviar", ["en"] = "Submit" }) { DefinitionRevision = 1 };
    public StructuredFormSelectionRow AlternativeForm { get; } = new(Guid.NewGuid(), Guid.NewGuid(), 1, "MAINT",
        new Dictionary<string,string> { ["es"] = "Registrar mantenimiento", ["en"] = "Register maintenance" }, 1,
        new Dictionary<string,string> { ["es"] = "Enviar", ["en"] = "Submit" }) { DefinitionRevision = 1 };
    public StructuredFormCheckRequest Snapshot => SnapshotFor(Form);
    public StructuredFormCheckRequest SnapshotFor(StructuredFormSelectionRow form) => new(1, "es", form.Name, form.SubmitLabel,
        [new(form.DefinitionId == AlternativeForm.DefinitionId ? AlternativeFieldId : FieldId, "field_" + (form.DefinitionId == AlternativeForm.DefinitionId ? AlternativeFieldId : FieldId).ToString("N"), "shortText", new Dictionary<string,string> { ["es"] = "Lugar", ["en"] = "Place" }, null, false, 80, null, null)], ["es", "en"]);
    public AssistantObservedProposal Proposal(Guid recipient) => ObservedProposal(recipient, "forms.propose-registration");
    public AssistantObservedProposal ClarificationProposal(Guid recipient) => ObservedProposal(recipient, "forms.clarify-registration");
    public AssistantObservedProposal SuggestionProposal(Guid recipient) => ObservedProposal(recipient, "forms.propose-suggestion");
    private AssistantObservedProposal ObservedProposal(Guid recipient, string capability)
    {
        var turn = Guid.NewGuid();
        var read = new AssistantObservedRead(turn, 1, new(capability == "forms.propose-suggestion" ? "forms.suggestion-candidates" : "forms.assistant-candidates", 1), new WhatsAppAssistantTestPayload());
        var proposal = new AssistantObservedProposal(turn, Guid.NewGuid(), new(capability, 1), new WhatsAppAssistantTestPayload()) { ObservedReads = [read] };
        _proposals.Add(proposal.Id, (recipient, proposal)); return proposal;
    }
    public async Task<Result<StructuredFormPrefillResponse>> ValidatePrefillAsync(StructuredFormPrefillRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsPublished || request.ForSending && !ValuesAvailableForSending
            || CanCompleteAsync is {} canComplete && !await canComplete(request.RecipientUserId, ct)
            || !_proposals.TryGetValue(request.Proposal.Id, out var registered) || registered.Recipient != request.RecipientUserId
            || !ReferenceEquals(registered.Proposal, request.Proposal)) return Result.Failure<StructuredFormPrefillResponse>(SystemQueryErrors.NotFound);
        var field = new StructuredFormPrefillField(FieldId, "field_" + FieldId.ToString("N"), "shortText", "Lugar", "Sucursal Norte", "Sucursal Norte");
        return Result.Success(new StructuredFormPrefillResponse(Form, 1, [field], [], "La persona contó una visita y mencionó el lugar.", "es"));
    }
    public Task<StructuredFormPreparationResponse> PrepareAsync(Guid recipientUserId, string recipientCulture, Guid definitionId, Guid versionId, bool forSending, CancellationToken ct) => Task.FromResult(new StructuredFormPreparationResponse([], []));
    public Task LockSelectionAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<StructuredFormSelectionRow?> FindAsync(Guid recipientUserId, string recipientCulture, Guid definitionId, Guid versionId, CancellationToken ct) =>
        Task.FromResult(IsPublished ? Selection(definitionId, versionId) : null);
    public Task<StructuredFormSelectionRow?> FindAcceptedSessionAsync(Guid recipientUserId, string recipientCulture, Guid definitionId, Guid versionId, CancellationToken ct) =>
        Task.FromResult(Selection(definitionId, versionId));
    public Task<PagedResult<StructuredFormSelectionRow>> ListAsync(Guid recipientUserId, string recipientCulture, ListStructuredFormsRequest request, CancellationToken ct) =>
        Task.FromResult(new PagedResult<StructuredFormSelectionRow>(IsPublished ? [Form, AlternativeForm] : [], request.Page, request.PageSize, IsPublished ? 2 : 0));
    public async Task<Result<StructuredFormSuggestionsResponse>> ListSuggestionsAsync(Guid recipientUserId,string recipientCulture,Guid sourceRecordId,CancellationToken ct)
    {
        if(SuggestionMode is not ("Always" or "Assistant") || !IsPublished
            || CanCompleteAsync is {} canComplete && !await canComplete(recipientUserId,ct)
            || CanUseCapturedSourceAsync is {} canUse && !await canUse(recipientUserId,sourceRecordId,ct)) return SystemQueryErrors.NotFound;
        var destination=SuggestionCreatesLoop ? Form : AlternativeForm;
        var field=MappedField(destination);
        return new StructuredFormSuggestionsResponse(sourceRecordId,SuggestionSetId,1,Form.Name["es"],Form.Prefix+"-000024",
            [new(FieldId,"field_"+FieldId.ToString("N"),"shortText","Lugar","Sucursal Norte")],
            [new(SuggestionId,SuggestionMode,SuggestionMode=="Assistant" ? "Cuando corresponda registrar mantenimiento" : null,destination,1,"es",
                SnapshotFor(destination).Questions,[],[field],SuggestionCreatesLoop)]);
    }
    public async Task<Result<StructuredFormSuggestionPreparationResponse>> PrepareSuggestionAsync(StructuredFormSuggestionPreparationRequest request,CancellationToken ct)
    {
        SuggestionPreparations.Add(request);
        if(request.ActorUserId is {} actor && (CanReadAsync is not {} canRead || !await canRead(actor,ct))) return SystemQueryErrors.Forbidden;
        var listed=await ListSuggestionsAsync(request.RecipientUserId,request.Culture,request.SourceRecordId,ct);
        if(listed.IsFailure || request.SuggestionSetId is {} set && set!=SuggestionSetId || request.SuggestionId is {} suggestion && suggestion!=SuggestionId
            || request.ForSending && (request.SuggestionSetId is null || request.SuggestionId is null || !ValuesAvailableForSending)) return SystemQueryErrors.NotFound;
        if(SuggestionMode=="Always" && request.Proposal is not null) return SystemQueryErrors.NotFound;
        if(SuggestionMode=="Assistant" && (request.Proposal is not {} proposal || proposal.Capability.Id!="forms.propose-suggestion"
            || !_proposals.TryGetValue(proposal.Id,out var registered) || registered.Recipient!=request.RecipientUserId || !ReferenceEquals(registered.Proposal,proposal)))
            return SystemQueryErrors.NotFound;
        var candidate=listed.Value.Candidates[0];var proposed=MappedField(candidate.Form);
        var included=request.IncludedFieldIds is null || request.IncludedFieldIds.Contains(proposed.FieldId);
        var prefill=new StructuredFormPrefillResponse(candidate.Form,1,included ? [proposed] : [],[],"Continuación de la visita confirmada.","es");
        return new StructuredFormSuggestionPreparationResponse(prefill,[proposed],new(request.SourceRecordId,SuggestionId,SuggestionSetId,
            [new("Field",FieldId,proposed.FieldId)]),SuggestionCreatesLoop);
    }
    private StructuredFormPrefillField MappedField(StructuredFormSelectionRow form)
    {
        var field=form.DefinitionId==AlternativeForm.DefinitionId ? AlternativeFieldId : FieldId;
        return new(field,"field_"+field.ToString("N"),"shortText","Lugar","Sucursal Norte","Sucursal Norte");
    }
    public async Task<Result<StructuredFormClarificationResponse>> ValidateClarificationAsync(StructuredFormClarificationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(!IsPublished || request.ForSending && !ValuesAvailableForSending
            || CanCompleteAsync is {} canComplete && !await canComplete(request.RecipientUserId,ct)
            || !_proposals.TryGetValue(request.Proposal.Id,out var registered) || registered.Recipient!=request.RecipientUserId
            || !ReferenceEquals(registered.Proposal,request.Proposal) || request.Proposal.Capability.Id!="forms.clarify-registration")
            return SystemQueryErrors.NotFound;
        var result=new StructuredFormClarificationResponse("¿Qué formulario querés completar?",
            [new(Form.DefinitionId,Form.VersionId,"Visita",1),new(AlternativeForm.DefinitionId,AlternativeForm.VersionId,"Mantenimiento",1)]);
        if(request.SelectedDefinitionId is not {} selected) return result;
        var form=selected==Form.DefinitionId ? Form : selected==AlternativeForm.DefinitionId ? AlternativeForm : null;
        return form is null ? Result.Failure<StructuredFormClarificationResponse>(SystemQueryErrors.NotFound)
            : Result.Success(result with {SelectedForm=new(form,1,[],[],"Eligió este formulario entre las opciones ofrecidas.","es")});
    }
    private StructuredFormSelectionRow? Selection(Guid definitionId,Guid versionId) =>
        new[] {Form,AlternativeForm}.SingleOrDefault(form=>form.DefinitionId==definitionId && form.VersionId==versionId);
}
