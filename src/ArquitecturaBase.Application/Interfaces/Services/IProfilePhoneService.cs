using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>
/// El número de la sesión: desvincularlo conservando otro medio de ingreso y las sesiones actuales y, con un correo
/// verificado, confirmándolo con el código que se le mandó a ese correo (N11; lo pide <see cref="IProfileService"/>).
/// </summary>
public interface IProfilePhoneService
{
    Task<Result> UnlinkOwnPhoneAsync(UnlinkOwnPhoneRequest request, CancellationToken cancellationToken);
}
