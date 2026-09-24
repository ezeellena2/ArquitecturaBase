using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface ILoginLinkService
{
    Task<Result<LoginLinkPreviewResponse>> PreviewAsync(
        PreviewLoginLinkRequest request, CancellationToken cancellationToken);

    Task<Result> RedeemAsync(RedeemLoginLinkRequest request, CancellationToken cancellationToken);
}
