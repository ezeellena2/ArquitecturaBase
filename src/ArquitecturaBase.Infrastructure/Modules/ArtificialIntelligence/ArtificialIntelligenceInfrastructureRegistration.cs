using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Repositories;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.OpenAI;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.Anthropic;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Places;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
public static class ArtificialIntelligenceInfrastructureRegistration
{
    public const string OpenAiHttpClientName = "AiOpenAI";
    public const string AnthropicHttpClientName = "AiAnthropic";
    public const string GooglePlacesHttpClientName = "AiGooglePlaces";
    public static IServiceCollection AddArtificialIntelligenceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(x => x.ServiceType == typeof(IAiSecretProtector))) return services;
        services.AddOptions<AiSecretProtectionOptions>().Bind(configuration.GetSection("ArtificialIntelligence:SecretProtection"))
            .Validate(x => x.IsValid(), "Configure ArtificialIntelligence:SecretProtection with an installation ID, active key ID and external 256-bit keys.").ValidateOnStart();
        services.AddOptions<AiTransportOptions>().Bind(configuration.GetSection("ArtificialIntelligence:Transport")).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<AiKeyRing>();
        services.AddSingleton<IAiSecretProtector, AesGcmAiSecretProtector>();
        services.AddSingleton<IAiChangeFingerprint, HmacAiChangeFingerprint>();
        services.AddSingleton<IAiBusinessInformationFingerprint, HmacAiBusinessInformationFingerprint>();
        // I17: a copy whose ring does not match the control values in the database is not ready.
        services.AddSingleton<IAiKeyRingControl, HmacAiKeyRingControl>();
        services.AddScoped<IAiKeyRingControlRepository, AiKeyRingControlRepository>();
        // I17, review: one verdict for the readiness, the worker's claim, the channels' availability and every acceptance.
        services.AddSingleton<AiKeyRingReadiness>();
        services.AddSingleton<IAiKeyRingReadiness>(provider => provider.GetRequiredService<AiKeyRingReadiness>());
        services.AddSingleton<AiKeyRingHealthCheck>();
        services.AddHealthChecks().AddCheck<AiKeyRingHealthCheck>(AiKeyRingHealthCheck.Name);
        services.AddScoped<IAiBusinessInformationRepository, AiBusinessInformationRepository>();
        services.AddScoped<IAiBusinessInformationReader, AiBusinessInformationReader>();
        services.AddScoped<IAiConnectionRepository, AiConnectionRepository>();
        services.AddScoped<IAiConnectionReader, AiConnectionReader>();
        services.AddScoped<IAiUsageReader, AiUsageReader>();
        services.AddScoped<IAiUsageRepository, AiUsageRepository>();
        services.AddAiAssistantProvidersAndProtection();
        services.AddScoped<IAiAssistantRepository, AiAssistantRepository>();
        services.AddScoped<IAiAssistantReader, AiAssistantReader>();
        services.AddOptions<AiAssistantOptions>().Bind(configuration.GetSection("ArtificialIntelligence:Assistant"))
            .Validate(options => options.IsValid(), "Configure valid assistant pilot limits and explicitly qualified model profiles.").ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<AiAssistantOptions>>().Value.Limits);
        services.AddSingleton<IReadOnlyCollection<AiAssistantModelProfile>>(provider => provider.GetRequiredService<IOptions<AiAssistantOptions>>().Value.ModelProfiles);
        services.AddSingleton<IReadOnlyCollection<AiAssistantAudioModelProfile>>(provider => provider.GetRequiredService<IOptions<AiAssistantOptions>>().Value.AudioModelProfiles);
        services.AddSingleton<IReadOnlyCollection<AiAssistantDocumentModelProfile>>(provider => provider.GetRequiredService<IOptions<AiAssistantOptions>>().Value.DocumentModelProfiles);
        services.AddSingleton<AiAssistantCoordinator>();
        services.AddSingleton<AiAssistantTelemetry>();
        services.AddHostedService<AiAssistantBackgroundService>();
        services.AddHostedService<AiAssistantRetentionBackgroundService>();
        // I10: the assistant's traces, with the context each turn keeps, beside its meters.
        services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter("ArquitecturaBase.AiAssistant", "ArquitecturaBase.AiAssistant.Maintenance", "ArquitecturaBase.AiAssistant.Application"))
            .WithTracing(tracing => tracing.AddSource(AiAssistantTracing.SourceName));
        services.AddSingleton<LlmHttpTransport>();
        services.AddSingleton<ILlmClient, LlmClient>();
        services.AddOptions<GooglePlacesOptions>().Bind(configuration.GetSection("ArtificialIntelligence:GooglePlaces")).ValidateDataAnnotations().ValidateOnStart();
        ConfigureClient(services.AddHttpClient(GooglePlacesHttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan));
        services.AddSingleton<IBusinessPlacesClient>(provider => new GooglePlacesClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(GooglePlacesHttpClientName),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<GooglePlacesOptions>>()));
        ConfigureClient(services.AddHttpClient<ILlmProviderClient, OpenAiResponsesClient>(OpenAiHttpClientName,
            client => { client.BaseAddress = new Uri("https://api.openai.com/"); client.Timeout = Timeout.InfiniteTimeSpan; }));
        ConfigureClient(services.AddHttpClient<ILlmProviderClient, AnthropicMessagesClient>(AnthropicHttpClientName,
            client => { client.BaseAddress = new Uri("https://api.anthropic.com/"); client.Timeout = Timeout.InfiniteTimeSpan; }));
        services.AddAiAssistantProvidersAndProtection();
        return services;
    }
    private static void ConfigureClient(IHttpClientBuilder client) => client.RemoveAllLoggers().RemoveAllResilienceHandlers()
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
}
