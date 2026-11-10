using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>The folder tree of each listing and its administration: create, rename, move, delete and the place in the menu.</summary>
public interface IFormFolderService
{
    Task<Result<FormFolderTreeResponse>> ListAsync(ListFormFoldersRequest request, CancellationToken cancellationToken);

    Task<Result<FormFolderResponse>> CreateAsync(CreateFormFolderRequest request, CancellationToken cancellationToken);

    Task<Result<FormFolderResponse>> UpdateAsync(Guid id, UpdateFormFolderRequest request, CancellationToken cancellationToken);

    Task<Result<FormFolderDeletionResponse>> GetDeletionAsync(Guid id, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(Guid id, DeleteFormFolderRequest request, CancellationToken cancellationToken);

    Task<Result<FormFolderResponse>> SetMenuAsync(Guid id, SetFormFolderMenuRequest request, CancellationToken cancellationToken);
}
