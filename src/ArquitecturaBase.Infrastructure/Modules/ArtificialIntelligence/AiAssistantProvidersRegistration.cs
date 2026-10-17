using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.Anthropic;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.OpenAI;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;

internal static class AiAssistantProvidersRegistration
{
    internal const string OpenAiConversationHttpClientName = "AiAssistantOpenAI";
    internal const string OpenAiAudioHttpClientName = "AiAssistantOpenAIAudio";
    internal const string AnthropicConversationHttpClientName = "AiAssistantAnthropic";

    internal static void AddAiAssistantProvidersAndProtection(this IServiceCollection services)
    {
        services.AddSingleton<IAiConversationProtector, AesGcmAiAssistantContentProtector>();
        services.AddSingleton<IAiAssistantRequestFingerprint, HmacAiAssistantRequestFingerprint>();
        services.AddSingleton<LlmConversationHttpTransport>();
        Configure(services.AddHttpClient<OpenAiConversationClient>(OpenAiConversationHttpClientName,
            client => { client.BaseAddress = new Uri("https://api.openai.com/"); client.Timeout = Timeout.InfiniteTimeSpan; }));
        Configure(services.AddHttpClient<AnthropicConversationClient>(AnthropicConversationHttpClientName,
            client => { client.BaseAddress = new Uri("https://api.anthropic.com/"); client.Timeout = Timeout.InfiniteTimeSpan; }));
        Configure(services.AddHttpClient<ILlmAudioTranscriptionClient, OpenAiAudioTranscriptionClient>(OpenAiAudioHttpClientName,
            client => { client.BaseAddress = new Uri("https://api.openai.com/"); client.Timeout = Timeout.InfiniteTimeSpan; }));
        services.AddTransient<ILlmConversationClient, LlmConversationClient>();
    }

    private static void Configure(IHttpClientBuilder client) => client.RemoveAllLoggers().RemoveAllResilienceHandlers()
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2) });
}
