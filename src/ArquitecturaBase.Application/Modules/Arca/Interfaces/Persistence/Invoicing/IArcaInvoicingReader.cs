using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;

public interface IArcaInvoicingReader
{
    Task<ArcaInvoicingSettingsRow?> FindSettingsAsync(Guid connectionId, CancellationToken cancellationToken);
}
