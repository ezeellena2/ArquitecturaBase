using ArquitecturaBase.Application.Models.StructuredData;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
public interface IWhatsAppRecordOriginReader
{
    Task<IReadOnlyList<StructuredRecordOrigin>> ListAsync(IReadOnlyCollection<Guid> sourceReceiptIds, bool canOpenConversation, CancellationToken cancellationToken);
}
