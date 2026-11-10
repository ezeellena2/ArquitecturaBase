using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormCatalogImportService
{
    Task<Result<FormCatalogImportPreviewResponse>> PreviewAsync(Guid id, FormCatalogImportRequest request, CancellationToken cancellationToken);
    Task<Result<FormCatalogSaveResponse>> ApplyAsync(Guid id, FormCatalogImportRequest request, CancellationToken cancellationToken);

    /// <summary>The same review for the spreadsheet a proposal of the assistant mapped, read from the private copy of its turn.</summary>
    Task<Result<FormCatalogImportPreviewResponse>> PreviewFromAssistantAsync(Guid id, FormCatalogAssistantImportRequest request, CancellationToken cancellationToken);

    Task<Result<FormCatalogSaveResponse>> ApplyFromAssistantAsync(Guid id, FormCatalogAssistantImportRequest request, CancellationToken cancellationToken);

    /// <summary>What a catalog created from a proposal with a spreadsheet still has to import, for the person who asked for it.</summary>
    Task<Result<FormCatalogAssistantImportResponse>> GetAssistantImportAsync(Guid id, CancellationToken cancellationToken);
}
