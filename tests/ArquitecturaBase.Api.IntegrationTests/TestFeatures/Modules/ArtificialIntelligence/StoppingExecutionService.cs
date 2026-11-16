using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

/// <summary>
/// I12: the real execution of a copy whose stop arrives while it prepares a turn, before any step: it runs <c>whilePreparing</c> and
/// then prepares as the module does. Only for the copy that stops; the other copy keeps the module's own registration.
/// </summary>
internal sealed class StoppingExecutionService(IAiAssistantExecutionService inner, Action whilePreparing) : IAiAssistantExecutionService
{
    public static void Decorate(IServiceCollection services, Action whilePreparing)
    {
        var real = services.Single(descriptor => descriptor.ServiceType == typeof(IAiAssistantExecutionService));
        services.Remove(real);
        services.Add(ServiceDescriptor.Describe(typeof(IAiAssistantExecutionService), provider => new StoppingExecutionService(
            (IAiAssistantExecutionService)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!), whilePreparing), real.Lifetime));
    }

    public Task<Result<AiAssistantExecutionContextResponse>> PrepareAsync(AiAssistantClaimResponse claim, CancellationToken cancellationToken)
    {
        whilePreparing();
        return inner.PrepareAsync(claim, cancellationToken);
    }

    public Task<Result<LlmConversationResponse>> CallProviderAsync(AiAssistantExecutionContextResponse context, CancellationToken cancellationToken) =>
        inner.CallProviderAsync(context, cancellationToken);

    public Task<Result<AiAssistantAdvanceResponse>> AdvanceAsync(AiAssistantExecutionContextResponse context, LlmConversationResponse response,
        CancellationToken cancellationToken) => inner.AdvanceAsync(context, response, cancellationToken);
}
