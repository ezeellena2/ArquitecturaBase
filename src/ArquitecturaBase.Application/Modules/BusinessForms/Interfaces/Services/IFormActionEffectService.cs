using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormActionEffectService
{
    Task<FormActionOutcomeResponse> ExecuteAsync(FormActionWorkResponse work,CancellationToken cancellationToken);
}
