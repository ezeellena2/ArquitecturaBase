using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IUserAdministrationService
{
    Task<Result<Guid>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken);

    Task<Result> SendInvitationAsync(SendUserInvitationRequest request, CancellationToken cancellationToken);
}
