using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>The folders in the side menu (CP5-A), pruned for the person reading it.</summary>
public interface IFormMenuService
{
    Task<Result<FormMenuResponse>> GetAsync(CancellationToken cancellationToken);
}
