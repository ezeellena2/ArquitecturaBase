using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormRecipientPreviewService
{
    Task<Result<PagedResult<FormCatalogFilterSampleUserResponse>>> ListSamplesAsync(ListFormRecipientSamplesRequest request, CancellationToken cancellationToken);
    Task<Result<FormRecipientPreviewResponse>> PreviewAsync(Guid definitionId, FormRecipientPreviewRequest request, CancellationToken cancellationToken);
}
