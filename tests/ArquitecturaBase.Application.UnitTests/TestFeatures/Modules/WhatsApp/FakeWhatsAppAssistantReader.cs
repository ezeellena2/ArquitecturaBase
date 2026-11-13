using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class FakeWhatsAppAssistantReader : IWhatsAppAssistantReader
{
    public HashSet<Guid> DeclinedDefinitions { get; } = [];
    /// <summary>The answers that went out for an attachment, by the question their reference continues (null: not a continuation).</summary>
    public HashSet<(Guid AttachmentMessageId, Guid? Continuing)> AttachmentAnswers { get; } = [];
    public IReadOnlyCollection<Guid>? RequestedDeclineDefinitions { get; private set; }
    public Task<IReadOnlyList<Guid>> ListDeclinedDefinitionIdsAsync(Guid subjectUserId, IReadOnlyCollection<Guid> definitionIds, CancellationToken ct)
    { RequestedDeclineDefinitions = definitionIds; return Task.FromResult<IReadOnlyList<Guid>>(definitionIds.Where(DeclinedDefinitions.Contains).Distinct().ToArray()); }
    public List<StructuredFormOfferReference> Offers { get; } = [];
    public bool CurrentContext {get;set;}=true;
    public Task<bool> ExistsCurrentContextAsync(WhatsAppAssistantReferenceRow reference,DateTime nowUtc,CancellationToken ct)=>Task.FromResult(CurrentContext);
    public bool CurrentIsVisible { get; set; } = true;
    public WhatsAppAssistantReferenceRow? Reference { get; set; }
    public WhatsAppAssistantChoiceRow? Choice {get;set;}
    public Task<WhatsAppAssistantChoiceRow?> FindLatestChoiceAsync(Guid contactId,CancellationToken ct)=>Task.FromResult(Choice);
    // IA3-A: the tap on an authoring summary, the latest summary's operation and the change that the next message brings.
    public WhatsAppAssistantChoiceRow? AuthoringTap {get;set;}
    public Task<WhatsAppAssistantChoiceRow?> FindLatestAuthoringTapAsync(Guid contactId,CancellationToken ct)=>Task.FromResult(AuthoringTap);
    public WhatsAppAssistantChoiceRow? CaseTap {get;set;}
    public Task<WhatsAppAssistantChoiceRow?> FindLatestCaseTapAsync(Guid contactId,CancellationToken ct)=>Task.FromResult(CaseTap);
    public Guid? LatestAuthoringRequest {get;set;}
    public Task<Guid?> FindLatestAuthoringRequestAsync(Guid contactId,CancellationToken ct)=>Task.FromResult(LatestAuthoringRequest);
    public Guid? ChangeRequest {get;set;}
    public Task<Guid?> FindChangeRequestAsync(Guid contactId,CancellationToken ct)=>Task.FromResult(ChangeRequest);
    // Task 9 of the documents: the read documents by their message, the tap on a document question and the latest question's operation.
    public Dictionary<Guid,WhatsAppAssistantDocumentRow> Documents { get; } = [];
    public Task<WhatsAppAssistantDocumentRow?> FindDocumentAsync(Guid contactId,Guid messageId,CancellationToken ct)=>
        Task.FromResult(Documents.TryGetValue(messageId,out var document)?document:null);
    public WhatsAppAssistantChoiceRow? DocumentTap {get;set;}
    public Task<WhatsAppAssistantChoiceRow?> FindLatestDocumentTapAsync(Guid contactId,CancellationToken ct)=>Task.FromResult(DocumentTap);
    public Guid? LatestDocumentRequest {get;set;}
    public Task<Guid?> FindLatestDocumentRequestAsync(Guid contactId,CancellationToken ct)=>Task.FromResult(LatestDocumentRequest);
    // Review of task 9: what the reader finds of a tap: a closed conversation, or the questions another tap already answered.
    public WhatsAppAssistantDocumentTapState DocumentTapState {get;set;}=WhatsAppAssistantDocumentTapState.Open;
    public HashSet<Guid> AnsweredDocumentRequests {get;}=[];
    public Task<WhatsAppAssistantDocumentTapState> FindDocumentTapStateAsync(WhatsAppAssistantReferenceRow question,Guid tapId,DateTime nowUtc,CancellationToken ct)=>
        Task.FromResult(DocumentTapState==WhatsAppAssistantDocumentTapState.Closed?DocumentTapState
            :AnsweredDocumentRequests.Contains(question.RequestId)?WhatsAppAssistantDocumentTapState.Answered:WhatsAppAssistantDocumentTapState.Open);
    public WhatsAppAssistantSourceRow? Source { get; set; }
    public WhatsAppOutgoingMessageRow? Outgoing { get; set; }
    public int OfferCount { get; set; }
    public int? ExcludedOfferCount {get;set;}
    public WhatsAppAssistantSourceRow? PinnedSource {get;set;}
    public (DateTime FromUtc,DateTime ToUtc,Guid? ExcludingRequestId)? LastCount {get;private set;}
    public bool Declined { get; set; }
    public bool HasUnresolvedOffer {get;set;}
    public Task<WhatsAppAssistantSuggestionPointerResponse?> FindCurrentAsync(Guid contactId,CancellationToken ct) => Task.FromResult<WhatsAppAssistantSuggestionPointerResponse?>(CurrentIsVisible && Reference is { } row ? new(row.RequestId,row.SourceMessageId,row.State):null);
    public Task<WhatsAppOutgoingMessageRow?> FindOutgoingAsync(Guid contactId,Guid requestId,CancellationToken ct) => Task.FromResult(Outgoing);
    public Task<WhatsAppAssistantReferenceRow?> FindByOutgoingAsync(Guid outgoingId,CancellationToken ct)=>Task.FromResult(Reference);
    // IA, Stage 2: the references of a conversation by their request, for the walk along the operations they continue.
    public Dictionary<Guid,WhatsAppAssistantReferenceRow> References { get; } = [];
    public Task<WhatsAppAssistantReferenceRow?> FindAsync(Guid contactId,Guid requestId,CancellationToken ct) => Task.FromResult(References.TryGetValue(requestId,out var row)?row:Reference);
    public Task<WhatsAppAssistantSourceRow?> FindSourceAsync(Guid contactId,Guid id,CancellationToken ct) => Task.FromResult(PinnedSource??Source);
    public Task<WhatsAppAssistantSourceRow?> FindLatestSourceAsync(Guid contactId,CancellationToken ct) => Task.FromResult(Source);
    // As the SQL asks it: any answer of the attachment, or only those of the continuations of a question.
    public Task<bool> ExistsAttachmentAnswerAsync(Guid contactId,Guid attachmentMessageId,CancellationToken ct,Guid? continuing=null) =>
        Task.FromResult(AttachmentAnswers.Any(answer => answer.AttachmentMessageId == attachmentMessageId && (continuing is null || answer.Continuing == continuing)));
    public Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc,CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([]);
    public Task<bool> ExistsPendingAsync(DateTime nowUtc,CancellationToken ct)=>throw new NotSupportedException();
    public Task<int> CountOffersAsync(Guid contactId,DateTime fromUtc,DateTime toUtc,CancellationToken ct,Guid? excludingRequestId=null) {LastCount=(fromUtc,toUtc,excludingRequestId);return Task.FromResult(excludingRequestId.HasValue && ExcludedOfferCount is {} excluded?excluded:OfferCount);}
    public Task<bool> ExistsUnresolvedOfferAsync(Guid contactId,Guid excludingRequestId,CancellationToken ct)=>Task.FromResult(HasUnresolvedOffer);
    public Task<bool> ExistsDeclinedAsync(Guid contactId,Guid definitionId,CancellationToken ct) => Task.FromResult(Declined || DeclinedDefinitions.Contains(definitionId));
    public Task<IReadOnlyList<StructuredFormOfferReference>> ListOffersAsync(Guid subjectUserId,CancellationToken ct) => Task.FromResult<IReadOnlyList<StructuredFormOfferReference>>(Offers);
}
