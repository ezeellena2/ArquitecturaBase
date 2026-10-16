using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormPdfDocumentService(IFormPdfDesignReader reader, IPrivateFileStore store, FormRecordAccessGuard access, ILogger<FormPdfDocumentService> logger) : IFormPdfDocumentService
{
    public Task<Result<FormPdfContentResponse>> GetAsync(Guid recordId, Guid documentId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormPdfContentResponse>>(logger, "ReadFormPdfDocument", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            var document = await reader.FindDocumentAsync(recordId, documentId, cancellationToken);
            if (document is null) return FormPdfErrors.NotFound;
            var stream = await store.OpenReadAsync(document.StorageKey, cancellationToken);
            if (stream is null) return FormPdfErrors.NotFound;
            try
            {
                if (await access.StillAllowedAsync(actor.Value, cancellationToken)) return new FormPdfContentResponse(stream, document.Name, document.Culture);
                await stream.DisposeAsync(); return FormDefinitionErrors.Forbidden;
            }
            catch { await stream.DisposeAsync(); throw; }
        });
}
