using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>
/// Hands the contact's taps on form action buttons to the installed <c>IStructuredRecordActionReplySink</c> owners, outside
/// any WhatsApp transaction, and then marks each one handled in its own boundary. The text an owner hands back with an applied tap
/// goes back to the contact once, as a text by the normal outgoing; nothing else is sent.
/// </summary>
public interface IWhatsAppFormActionReplyService
{
    /// <summary>The contact's taps not handled yet, oldest first, up to a round: the processor hands each one over on its own (H21).</summary>
    Task<IReadOnlyList<Guid>> ListPendingAsync(Guid contactId, CancellationToken cancellationToken);

    /// <summary>One tap, if still pending: to its owners with its id and, as soon as they return, its answer queued and the tap marked.</summary>
    Task<Result> ProcessAsync(Guid contactId, Guid tapId, CancellationToken cancellationToken);
}
