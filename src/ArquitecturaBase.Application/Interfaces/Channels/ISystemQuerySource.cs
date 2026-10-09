using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>Typed, bounded local read sources. Identity and scope are supplied by the server.</summary>
public interface ISystemQuerySource
{
    IReadOnlyList<SystemQueryDefinition> Definitions { get; }
    Task<Result<SystemQueryResponse>> ExecuteAsync(SystemQueryRequest request, CancellationToken cancellationToken);
}
