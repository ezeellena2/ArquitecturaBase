using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
public interface IWhatsAppFormActionReader
{
    Task<IReadOnlyList<WhatsAppFormActionObservationRow>> ListObservationsAsync(IReadOnlyCollection<StructuredRecordActionObservationRequest> requests,CancellationToken cancellationToken);
    /// <summary>The WhatsApp number of the contact linked to the account, without locking it; null without a linked contact.</summary>
    Task<string?> FindLinkedWaIdAsync(Guid userId,CancellationToken cancellationToken);
    /// <summary>
    /// The contact's processed taps on a form action button (<c>FORM_ACTION:</c>) not yet handled, received within the pending
    /// window, oldest first.
    /// </summary>
    Task<IReadOnlyList<WhatsAppFormActionTapRow>> ListPendingRepliesAsync(Guid contactId,DateTime nowUtc,int limit,CancellationToken cancellationToken);
    /// <summary>Contacts with such a tap, looked up from the form actions sent with buttons.</summary>
    Task<IReadOnlyList<Guid>> ListContactsWithPendingRepliesAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc,CancellationToken cancellationToken);
    /// <summary>The form action with buttons that has this outgoing id; null for any other row.</summary>
    Task<WhatsAppFormActionReplyTargetRow?> FindReplyTargetAsync(Guid outgoingId,CancellationToken cancellationToken);
}
