using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>
/// Hands the contact's taps on form action buttons to the installed <c>IStructuredRecordActionReplySink</c> owners, outside
/// any WhatsApp transaction, and then marks them handled in its own boundary. Sends nothing to the contact.
/// </summary>
public interface IWhatsAppFormActionReplyService
{
    Task<Result> ProcessAsync(Guid contactId, CancellationToken cancellationToken);
}
