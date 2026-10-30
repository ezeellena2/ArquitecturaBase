using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
public interface IWhatsAppFormActionReader
{
    Task<IReadOnlyList<WhatsAppFormActionObservationRow>> ListObservationsAsync(IReadOnlyCollection<StructuredRecordActionObservationRequest> requests,CancellationToken cancellationToken);
    /// <summary>The WhatsApp number of the contact linked to the account, without locking it; null without a linked contact.</summary>
    Task<string?> FindLinkedWaIdAsync(Guid userId,CancellationToken cancellationToken);
}
