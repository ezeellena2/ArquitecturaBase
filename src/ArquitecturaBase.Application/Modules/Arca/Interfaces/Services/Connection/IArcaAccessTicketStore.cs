using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;

/// <summary>
/// Los pasos guardados del ticket compartido, cada uno en su transacción corta con el lock del ticket. Los llama el
/// proveedor de tickets, que pide a ARCA entre uno y otro; guardar, bloquear y soltar sólo valen para el dueño de la
/// reserva y devuelven false si otro la tomó.
/// </summary>
public interface IArcaAccessTicketStore
{
    Task<ArcaTicketLeaseResponse> LeaseAsync(Guid certificateId, ArcaServiceCode service, Guid owner, TimeSpan margin,
        TimeSpan leaseDuration, CancellationToken cancellationToken);

    Task<bool> StoreAsync(Guid certificateId, ArcaServiceCode service, Guid owner, string protectedCredentials, DateTime expiresAtUtc,
        CancellationToken cancellationToken);

    Task<bool> BlockAsync(Guid certificateId, ArcaServiceCode service, Guid owner, DateTime untilUtc, CancellationToken cancellationToken);

    Task<bool> ReleaseAsync(Guid certificateId, ArcaServiceCode service, Guid owner, CancellationToken cancellationToken);
}
