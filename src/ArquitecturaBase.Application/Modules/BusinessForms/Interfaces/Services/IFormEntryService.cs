using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>Loads a response in the app with «Completar formularios», in the name of whoever loads it.</summary>
public interface IFormEntryService
{
    Task<Result<FormEntryResponse>> GetAsync(Guid definitionId, CancellationToken cancellationToken);

    Task<Result<PagedResult<FormEntryOptionResponse>>> ListOptionsAsync(Guid definitionId, Guid versionId, Guid fieldId,
        ListFormEntryOptionsRequest request, CancellationToken cancellationToken);

    Task<Result<FormEntrySubmitResponse>> SubmitAsync(Guid definitionId, SubmitFormEntryRequest request, CancellationToken cancellationToken);
}
