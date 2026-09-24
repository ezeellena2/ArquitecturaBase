using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IProfileService
{
    Task<Result<CurrentUserResponse>> GetAsync(CancellationToken cancellationToken);

    Task<Result> UpdateAsync(UpdateProfileRequest request, CancellationToken cancellationToken);
}
