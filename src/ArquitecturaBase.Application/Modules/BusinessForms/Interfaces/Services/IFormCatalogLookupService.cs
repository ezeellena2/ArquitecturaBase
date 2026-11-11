using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// «Completar con ARCA» (CA1): a catalog that a module's registry completes (by its Key) fills an option from its tax id. Only reads:
/// the person saves the option as always.
/// </summary>
public interface IFormCatalogLookupService
{
    Task<Result<FormCatalogLookupResponse>> GetAsync(Guid catalogId, CancellationToken cancellationToken);

    Task<Result<FormCatalogLookupResultResponse>> LookupAsync(Guid catalogId, LookupFormCatalogPartyRequest request, CancellationToken cancellationToken);
}
