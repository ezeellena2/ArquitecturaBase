using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>«Cómo viene leyendo» (DOC1): how the reading of documents of a form did over the last 30 days, and its latest corrections.</summary>
public interface IFormDocumentReadingStatsService
{
    Task<Result<FormDocumentReadingStatsResponse>> GetAsync(Guid definitionId, CancellationToken cancellationToken);
}
