using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Validation;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Interfaces.Channels;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence;
public static class ArtificialIntelligenceApplicationRegistration
{
    public static IServiceCollection AddArtificialIntelligenceApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(new AiAssistantLimits());
        services.TryAddScoped<IAiConnectionService, AiConnectionService>();
        services.TryAddScoped<IAiAssistantService, AiAssistantService>();
        services.TryAddScoped<IAiAssistantDispatchService, AiAssistantDispatchService>();
        services.TryAddScoped<IAiAssistantExecutionService, AiAssistantExecutionService>();
        services.TryAddScoped<IAiAssistantRetentionService, AiAssistantRetentionService>();
        services.TryAddScoped<AiAssistantAccessGuard>();
        services.TryAddScoped<AiAssistantResponsePolicy>();
        services.TryAddScoped<AiAssistantReadPolicy>();
        services.TryAddScoped<AiAssistantSubmissionPolicy>();
        services.TryAddScoped<AiAssistantDispatchGuard>();
        services.TryAddScoped<AiAssistantContextPolicy>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISystemQuerySource, AiBusinessSystemQuerySource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<Models.SubmitAiAssistantTurnRequest>, SubmitAiAssistantTurnRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<Models.CancelAiAssistantTurnRequest>, CancelAiAssistantTurnRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<Models.ListAiConversationsRequest>, ListAiConversationsRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<Models.ListAiAssistantTurnsRequest>, ListAiAssistantTurnsRequestValidator>());
        services.TryAddScoped<IAiBusinessInformationService, AiBusinessInformationService>();
        services.TryAddScoped<IBusinessPlacesService, BusinessPlacesService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<Models.SearchBusinessPlacesRequest>, SearchBusinessPlacesRequestValidator>());
        services.TryAddScoped<IAiConnectionTestService, AiConnectionTestService>();
        services.TryAddScoped<AiConnectionCandidatePolicy>();
        services.TryAddScoped<AiConnectionChangeRecorder>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, AiPermissionCatalogSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IClientCapabilitySource, AiAssistantClientCapabilitySource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<Models.UpdateAiConnectionRequest>, UpdateAiConnectionRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<Models.UpdateAiBusinessInformationRequest>, UpdateAiBusinessInformationRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<Models.TestAiConnectionRequest>, TestAiConnectionRequestValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IValidator<Models.ListAiModelsRequest>, ListAiModelsRequestValidator>());
        return services;
    }
}
