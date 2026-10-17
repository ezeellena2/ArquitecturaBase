using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
public interface IWhatsAppFormActionReader
{
    Task<IReadOnlyList<WhatsAppFormActionObservationRow>> ListObservationsAsync(IReadOnlyCollection<StructuredRecordActionObservationRequest> requests,CancellationToken cancellationToken);
}
