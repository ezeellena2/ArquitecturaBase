using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>ME2: the logo and the icon of the installation, saved when uploaded or removed and served without a session.</summary>
public interface IBrandImageService
{
    Task<Result<BrandImageResponse>> UploadAsync(UploadBrandImageRequest request, CancellationToken cancellationToken);

    Task<Result<BrandImageResponse>> RemoveAsync(BrandImageKind kind, long expectedRevision, CancellationToken cancellationToken);

    Task<Result<BrandImageFileResponse>> OpenAsync(BrandImageKind kind, CancellationToken cancellationToken);
}
