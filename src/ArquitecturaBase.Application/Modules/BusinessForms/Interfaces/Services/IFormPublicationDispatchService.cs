using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormPublicationDispatchService
{
    Task<IReadOnlyList<Guid>> ListDueOperationIdsAsync(CancellationToken cancellationToken);
    Task<Result<FormPublicationWorkResponse?>> ReserveAsync(Guid? operationId, CancellationToken cancellationToken);
    Task<Result> CompleteAsync(FormPublicationWorkResponse work, IReadOnlyList<FormPublicationChannelProgress> progress, CancellationToken cancellationToken);
}
