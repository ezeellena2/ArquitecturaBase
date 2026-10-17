using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class FakeAssistantForms : IStructuredFormSource
{
    public IReadOnlyList<StructuredFormSelectionRow>? AcceptedForms {get;set;}
    public Result<StructuredFormPrefillResponse> Prefill {get;set;}=FlowErrors.StateConflict;
    public Result<StructuredFormPrefillResponse>? ForSendingPrefill {get;set;}
    public int Locks {get;private set;}
    public StructuredFormPrefillRequest? LastPrefill {get;private set;}
    public int Previews {get;private set;}
    public Task<Result<StructuredFormPrefillResponse>> ValidatePrefillAsync(StructuredFormPrefillRequest request,CancellationToken ct)
    { LastPrefill=request;Previews++;return Task.FromResult(request.ForSending && ForSendingPrefill is {} sending ? sending : Prefill); }
    public Task<Result<StructuredFormClarificationResponse>> ValidateClarificationAsync(StructuredFormClarificationRequest request,CancellationToken ct)=>throw new NotSupportedException();
    public Task<Result<StructuredFormSuggestionsResponse>> ListSuggestionsAsync(Guid userId,string culture,Guid recordId,CancellationToken ct)=>throw new NotSupportedException();
    public Task<Result<StructuredFormSuggestionPreparationResponse>> PrepareSuggestionAsync(StructuredFormSuggestionPreparationRequest request,CancellationToken ct)=>throw new NotSupportedException();
    public Task<StructuredFormPreparationResponse> PrepareAsync(Guid userId,string culture,Guid definitionId,Guid versionId,bool forSending,CancellationToken ct)=>throw new NotSupportedException();
    public Task LockSelectionAsync(CancellationToken ct){Locks++;return Task.CompletedTask;}
    public Task<StructuredFormSelectionRow?> FindAsync(Guid userId,string culture,Guid definitionId,Guid versionId,CancellationToken ct)=>Task.FromResult(Prefill.IsSuccess && Prefill.Value.Form.DefinitionId==definitionId && Prefill.Value.Form.VersionId==versionId ? Prefill.Value.Form : null);
    public Task<StructuredFormSelectionRow?> FindAcceptedSessionAsync(Guid userId,string culture,Guid definitionId,Guid versionId,CancellationToken ct)=>AcceptedForms is {} accepted?Task.FromResult(accepted.SingleOrDefault(form=>form.DefinitionId==definitionId&&form.VersionId==versionId)):FindAsync(userId,culture,definitionId,versionId,ct);
    public Task<PagedResult<StructuredFormSelectionRow>> ListAsync(Guid userId,string culture,ListStructuredFormsRequest request,CancellationToken ct)=>throw new NotSupportedException();
}
