using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class FakeWhatsAppAssistantReader : IWhatsAppAssistantReader
{
    public Task<IReadOnlyList<Guid>> ListCapturedSourceRecordIdsAsync(Guid subjectUserId,CancellationToken ct)=>Task.FromResult<IReadOnlyList<Guid>>([]);
    public List<StructuredFormOfferReference> Offers { get; } = [];
    public bool CurrentContext {get;set;}=true;
    public Task<bool> ExistsCurrentContextAsync(WhatsAppAssistantReferenceRow reference,DateTime nowUtc,CancellationToken ct)=>Task.FromResult(CurrentContext);
    public bool CurrentIsVisible { get; set; } = true;
    public WhatsAppAssistantReferenceRow? Reference { get; set; }
    public WhatsAppAssistantChoiceRow? Choice {get;set;}
    public Task<WhatsAppAssistantChoiceRow?> FindLatestChoiceAsync(Guid contactId,CancellationToken ct)=>Task.FromResult(Choice);
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
    public Task<WhatsAppAssistantReferenceRow?> FindAsync(Guid contactId,Guid requestId,CancellationToken ct) => Task.FromResult(Reference);
    public Task<WhatsAppAssistantSourceRow?> FindSourceAsync(Guid contactId,Guid id,CancellationToken ct) => Task.FromResult(PinnedSource??Source);
    public Task<WhatsAppAssistantSourceRow?> FindLatestSourceAsync(Guid contactId,CancellationToken ct) => Task.FromResult(Source);
    public Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc,CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([]);
    public Task<int> CountOffersAsync(Guid contactId,DateTime fromUtc,DateTime toUtc,CancellationToken ct,Guid? excludingRequestId=null) {LastCount=(fromUtc,toUtc,excludingRequestId);return Task.FromResult(excludingRequestId.HasValue && ExcludedOfferCount is {} excluded?excluded:OfferCount);}
    public Task<bool> ExistsUnresolvedOfferAsync(Guid contactId,Guid excludingRequestId,CancellationToken ct)=>Task.FromResult(HasUnresolvedOffer);
    public Task<bool> ExistsDeclinedAsync(Guid contactId,Guid definitionId,CancellationToken ct) => Task.FromResult(Declined);
    public Task<IReadOnlyList<StructuredFormOfferReference>> ListOffersAsync(Guid subjectUserId,CancellationToken ct) => Task.FromResult<IReadOnlyList<StructuredFormOfferReference>>(Offers);
}
