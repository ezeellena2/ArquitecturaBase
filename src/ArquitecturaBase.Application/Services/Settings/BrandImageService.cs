using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Caching;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Application.Validation.Settings;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Settings;

/// <summary>
/// ME2b: the logo and the icon are saved when uploaded or removed, with the revision the screen read, the settings lock and one
/// commit, like the PATCH. The file is written before the transaction with a new key: if the transaction does not commit, the
/// file is not referenced and the daily cleanup removes it; a replaced one goes the same way, because only the current keys
/// are kept (<see cref="Channels.BrandImageRetentionSource"/>).
/// </summary>
internal sealed class BrandImageService(
    ISystemSettingsRepository repository,
    ISystemSettingsCache cache,
    IUnitOfWork unitOfWork,
    IRequestValidator validator,
    IPrivateFileStore files,
    IImageInspector inspector,
    ILogger<BrandImageService> logger) : IBrandImageService
{
    public Task<Result<BrandImageResponse>> UploadAsync(UploadBrandImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result<BrandImageResponse>>(logger, "UploadBrandImage", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError) return validationError;
            if (inspector.Inspect(request.Content) is not { } image
                || !BrandImage.Allows(request.Kind, image.Format, image.Width, image.Height))
            {
                return FieldErrors.On(UploadBrandImageRequestValidator.InvalidFor(request.Kind), "file");
            }

            if (!files.IsAvailable) return FieldErrors.On(FileErrors.StorageUnavailable, "file");

            var key = BrandImage.NewKey(request.Kind, Guid.NewGuid(), image.Format);
            await using (var content = new MemoryStream(request.Content, writable: false))
            {
                await files.WriteOnceAsync(key, content, BrandImage.ContentTypeOf(key), cancellationToken);
            }

            return await SaveAsync(request.Kind, key, request.ExpectedRevision, cancellationToken);
        });
    }

    public Task<Result<BrandImageResponse>> RemoveAsync(BrandImageKind kind, long expectedRevision, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<BrandImageResponse>>(logger, "RemoveBrandImage", async () =>
            expectedRevision <= 0
                ? FieldErrors.Validation("expectedRevision", ValidationMessages.SettingsRevisionInvalid)
                : await SaveAsync(kind, null, expectedRevision, cancellationToken));

    public Task<Result<BrandImageFileResponse>> OpenAsync(BrandImageKind kind, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<BrandImageFileResponse>>(logger, "OpenBrandImage", async () =>
        {
            var settings = await repository.GetAsync(cancellationToken);
            var key = kind == BrandImageKind.Logo ? settings?.LogoFileKey : settings?.IconFileKey;
            if (!BrandImage.IsKey(kind, key) || !files.IsAvailable) return FileErrors.NotFound;
            var content = await files.OpenReadAsync(key, cancellationToken);
            return content is null
                ? FileErrors.Missing
                : new BrandImageFileResponse(content, BrandImage.ContentTypeOf(key), BrandImage.VersionOf(key));
        });

    private async Task<Result<BrandImageResponse>> SaveAsync(BrandImageKind kind, string? key, long expectedRevision,
        CancellationToken cancellationToken)
    {
        var result = await unitOfWork.ExecuteInTransactionAsync<Result<BrandImageResponse>>(async ct =>
        {
            await repository.LockAsync(ct);
            var settings = await repository.GetAsync(ct);
            if (settings is null) return SettingsErrors.NotFound;
            if (settings.Revision != expectedRevision) return SettingsErrors.RevisionConflict;
            var changed = settings.SetBrandImage(kind, key);
            return changed.IsFailure
                ? changed.Error
                : new BrandImageResponse(settings.Revision, key is null ? null : BrandImage.VersionOf(key));
        }, CommitPolicy.OnSuccess, cancellationToken);

        if (result.IsSuccess) await cache.InvalidateAsync(cancellationToken);
        return result;
    }
}
