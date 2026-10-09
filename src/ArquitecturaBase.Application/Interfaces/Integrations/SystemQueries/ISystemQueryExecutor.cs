using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Integrations.SystemQueries;

/// <summary>Executes each local read with its own technical scope and lifetime.</summary>
public interface ISystemQueryExecutor
{
    Task<Result<SystemQueryResponse>> ExecuteAsync(SystemQueryRequest request, CancellationToken cancellationToken);
}
