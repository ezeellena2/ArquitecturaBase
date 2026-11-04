using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;

/// <summary>La configuración de Facturación. Quien escribe toma antes el lock de la conexión.</summary>
public interface IArcaInvoicingRepository
{
    Task<ArcaInvoicingSettings?> GetSettingsAsync(Guid connectionId, CancellationToken cancellationToken);
    void Add(ArcaInvoicingSettings settings);
}
