using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>The library of standards (BI1-A): what the installed modules bring and «Agregar», which creates an own draft.</summary>
public interface IFormLibraryService
{
    Task<Result<FormLibraryResponse>> ListAsync(CancellationToken cancellationToken);
    Task<Result<FormDraftCommitResponse>> AddAsync(string key, AddFormStandardRequest request, CancellationToken cancellationToken);
}
