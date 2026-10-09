using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.SystemQueries;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.SystemQueries;

internal sealed class ScopedSystemQueryExecutor(IServiceScopeFactory scopeFactory) : ISystemQueryExecutor
{
    public async Task<Result<SystemQueryResponse>> ExecuteAsync(SystemQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var scope = scopeFactory.CreateAsyncScope();
        var matches = scope.ServiceProvider.GetServices<ISystemQuerySource>().Where(source => source.Definitions.Any(definition => definition.Id == request.ToolId)).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("Duplicate system query sources are not allowed.");
        return matches.Length == 0 ? SystemQueryErrors.NotFound : await matches[0].ExecuteAsync(request, cancellationToken);
    }
}
