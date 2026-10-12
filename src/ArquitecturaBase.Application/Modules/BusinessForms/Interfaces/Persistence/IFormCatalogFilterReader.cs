using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormCatalogFilterReader
{
    Task<IReadOnlyList<string>> ListRoleNamesAsync(CancellationToken cancellationToken);

    Task<PagedResult<FormCatalogFilterSampleUserResponse>> ListSampleUsersAsync(
        FormCatalogFilterOptionsRequest request, CancellationToken cancellationToken);

    Task<bool> ExistsActiveSampleUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<FormCatalogFilterPreviewResponse> FindPreviewAsync(
        Guid sampleUserId, FormCatalogFilterDefinition filters, CancellationToken cancellationToken);
}
