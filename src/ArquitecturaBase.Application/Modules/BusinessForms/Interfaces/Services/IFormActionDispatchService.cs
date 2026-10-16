using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormActionDispatchService
{
    Task<IReadOnlyList<Guid>> ListDueIdsAsync(CancellationToken cancellationToken);
    Task<Result<FormActionWorkResponse?>> ReserveAsync(Guid runId,CancellationToken cancellationToken);
    Task<Result> CompleteAsync(FormActionWorkResponse work,FormActionOutcomeResponse outcome,CancellationToken cancellationToken);
}
