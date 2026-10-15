using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormFileService(IFormFileReader reader, IStoredFileReader files, IPrivateFileStore store,
    ICurrentUser currentUser, IPermissionService permissions, ILogger<FormFileService> logger) : IFormFileService
{
    public Task<Result<FormFileContentResponse>> OpenAsync(Guid recordId, Guid fileId, bool thumbnail, bool download, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormFileContentResponse>>(logger, "ReadFormFile", async () =>
        {
            if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
            if (!await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, cancellationToken)) return FormDefinitionErrors.Forbidden;
            var reference = await reader.FindAsync(recordId, fileId, cancellationToken);
            if (reference is null) return FileErrors.NotFound;
            var file = await files.FindAsync(fileId, cancellationToken);
            if (file is null || thumbnail && (!reference.IsPhoto || file.ThumbnailKey is null)) return FileErrors.NotFound;
            var stream = await store.OpenReadAsync(thumbnail ? file.ThumbnailKey! : file.StorageKey, cancellationToken);
            try
            {
                if (!await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.RecordsRead, cancellationToken))
                {
                    await stream.DisposeAsync();
                    return FormDefinitionErrors.Forbidden;
                }
                return new FormFileContentResponse(stream, thumbnail ? "image/jpeg" : file.ContentType,
                    thumbnail || reference.IsPhoto && !download ? null : file.Name);
            }
            catch
            {
                await stream.DisposeAsync();
                throw;
            }
        });
}
