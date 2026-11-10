using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>The folders of a form or a catalog (CP2-A): read them and save the whole set against the one that was read.</summary>
public interface IFormFolderItemService
{
    Task<Result<FormItemFoldersResponse>> GetAsync(FormFolderItemKind kind, Guid itemId, CancellationToken cancellationToken);

    Task<Result<FormItemFoldersResponse>> SetAsync(FormFolderItemKind kind, Guid itemId, SetFormItemFoldersRequest request,
        CancellationToken cancellationToken);
}
