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

    /// <summary>What a correction opens (CW10-A): the active version with the response as it was, for whoever may correct it.</summary>
    Task<Result<FormEntryCorrectionResponse>> GetCorrectionAsync(Guid definitionId, Guid recordId, CancellationToken cancellationToken);

    /// <summary>Saves a correction: another record with the same number replaces the response, which stays in its history.</summary>
    Task<Result<FormEntrySubmitResponse>> CorrectAsync(Guid definitionId, Guid recordId, CorrectFormEntryRequest request, CancellationToken cancellationToken);
}
