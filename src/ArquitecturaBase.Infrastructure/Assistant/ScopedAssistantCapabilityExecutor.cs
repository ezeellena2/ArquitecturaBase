using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Assistant;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Assistant;

internal sealed class ScopedAssistantCapabilityExecutor(IServiceScopeFactory scopeFactory) : IAssistantCapabilityExecutor
{
    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var scope = scopeFactory.CreateAsyncScope();
        var matches = scope.ServiceProvider.GetServices<IAssistantCapabilitySource>()
            .SelectMany(source => source.Definitions.Where(definition => definition.Id == request.Capability.Id
                && definition.Version == request.Capability.Version).Select(_ => source)).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("Duplicate assistant capability sources are not allowed.");
        return matches.Length == 0 ? SystemQueryErrors.NotFound : await matches[0].ExecuteAsync(request, cancellationToken);
    }
}
