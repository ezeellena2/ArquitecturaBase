using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>
/// Dentro del límite de quien guarda: bloquea la conexión, trae la configuración de Facturación (la crea si todavía no
/// existe, con revisión 0) y la devuelve sólo si sigue en la revisión que se leyó. No guarda ni abre transacciones.
/// </summary>
internal sealed class ArcaInvoicingSettingsGuard(IArcaConnectionRepository connections, IArcaInvoicingRepository repository)
{
    public async Task<Result<ArcaInvoicingSettings>> LockAsync(long expectedRevision, CancellationToken cancellationToken)
    {
        await connections.LockConnectionAsync(cancellationToken);
        if (await connections.GetConnectionAsync(cancellationToken) is not { } connection) return ArcaConnectionErrors.NotConnected;
        if (await repository.GetSettingsAsync(connection.Id, cancellationToken) is not { } settings)
        {
            settings = ArcaInvoicingSettings.Create(connection.Id);
            repository.Add(settings);
        }

        var current = settings.EnsureRevision(expectedRevision);
        return current.IsFailure ? current.Error : settings;
    }
}
