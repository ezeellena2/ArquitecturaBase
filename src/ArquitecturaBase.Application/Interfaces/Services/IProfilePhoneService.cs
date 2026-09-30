using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>El número de la sesión: desvincularlo conservando otro medio de ingreso y las sesiones actuales.</summary>
public interface IProfilePhoneService
{
    Task<Result> UnlinkOwnPhoneAsync(CancellationToken cancellationToken);
}
