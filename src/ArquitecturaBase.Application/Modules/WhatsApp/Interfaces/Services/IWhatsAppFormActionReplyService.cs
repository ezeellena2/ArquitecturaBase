using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>
/// Hands the contact's taps on form action buttons to the installed <c>IStructuredRecordActionReplySink</c> owners, outside
/// any WhatsApp transaction, and then marks them handled in its own boundary. The text an owner hands back with an applied tap
/// goes back to the contact once, as a text by the normal outgoing; nothing else is sent.
/// </summary>
public interface IWhatsAppFormActionReplyService
{
    Task<Result> ProcessAsync(Guid contactId, CancellationToken cancellationToken);
}
