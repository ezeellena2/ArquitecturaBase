using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IUserAdministrationService
{
    /// <summary>
    /// El alta: una cuenta nueva o, si el correo o el número son de una borrada, esa misma restaurada sólo con lo que trae
    /// el alta (<see cref="CreateUserResponse.Restored"/>).
    /// </summary>
    Task<Result<CreateUserResponse>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken);

    Task<Result> SendInvitationAsync(SendUserInvitationRequest request, CancellationToken cancellationToken);
}
