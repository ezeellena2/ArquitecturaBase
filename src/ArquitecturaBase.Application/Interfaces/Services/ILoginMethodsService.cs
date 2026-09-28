using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>Con qué se puede entrar: Google y WhatsApp, según la configuración.</summary>
public interface ILoginMethodsService
{
    Task<Result<LoginMethodsResponse>> GetLoginMethodsAsync(CancellationToken cancellationToken);
}
