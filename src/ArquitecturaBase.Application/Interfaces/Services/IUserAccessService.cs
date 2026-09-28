using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IUserAccessService
{
    Task<Result> SetUserActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);

    Task<Result> DeleteUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result> UnlinkUserPhoneAsync(Guid userId, CancellationToken cancellationToken);
}
