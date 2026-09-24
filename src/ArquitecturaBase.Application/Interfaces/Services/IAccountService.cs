using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IAccountService
{
    Task<Result<LoginMethodsResponse>> GetLoginMethodsAsync(CancellationToken cancellationToken);

    Task<Result<RequestLoginCodeResponse>> RequestLoginCodeAsync(
        RequestLoginCodeRequest request,
        CancellationToken cancellationToken);

    Task<Result<RequestWhatsAppLoginCodeResponse>> RequestWhatsAppLoginCodeAsync(
        RequestWhatsAppLoginCodeRequest request,
        CancellationToken cancellationToken);

    Task<Result<VerifyLoginCodeResponse>> VerifyLoginCodeAsync(
        VerifyLoginCodeRequest request,
        CancellationToken cancellationToken);
}
