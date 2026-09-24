using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IAccountService
{
    Task<Result<LoginMethodsResponse>> GetLoginMethodsAsync(CancellationToken cancellationToken);
}
