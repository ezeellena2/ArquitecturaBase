using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

internal sealed class AiTestModule : IApiTestModule
{
    public IReadOnlyCollection<string> Routes => ["GET /api/ai/connection", "PUT /api/ai/connection",
        "GET /api/ai/connection/changes/{requestid:guid}", "POST /api/ai/connection/test", "POST /api/ai/connection/models",
        "GET /api/ai/business-information", "PUT /api/ai/business-information", "GET /api/ai/business-information/changes/{requestid:guid}", "POST /api/ai/business-information/places/search",
        "GET /api/ai/assistant/bootstrap", "GET /api/ai/assistant/conversations", "GET /api/ai/assistant/conversations/{conversationid:guid}",
        "GET /api/ai/assistant/conversations/{conversationid:guid}/turns", "POST /api/ai/assistant/turns", "GET /api/ai/assistant/turns/{requestid:guid}",
        "POST /api/ai/assistant/turns/{requestid:guid}/cancel", "DELETE /api/ai/assistant/conversations/{conversationid:guid}"];
    public void ConfigureSettings(IWebHostBuilder builder)
    {
        builder.UseSetting("ArtificialIntelligence:SecretProtection:InstallationId", "integration-installation");
        builder.UseSetting("ArtificialIntelligence:SecretProtection:ActiveKeyId", "test");
        builder.UseSetting("ArtificialIntelligence:SecretProtection:Keys:test", ApiFactory.TestHashKey);
        builder.UseSetting("ArtificialIntelligence:RateLimiting:TestPermitLimit", "100000");
        builder.UseSetting("ArtificialIntelligence:RateLimiting:WritePermitLimit", "100000");
        builder.UseSetting("ArtificialIntelligence:RateLimiting:ReadPermitLimit", "100000");
        builder.UseSetting("ArtificialIntelligence:RateLimiting:PlacesPermitLimit", "100000");
        builder.UseSetting("ArtificialIntelligence:GooglePlaces:ApiKey", "fictional-key");
        builder.UseSetting("ArtificialIntelligence:Assistant:ProcessInBackground", "false");
        builder.UseSetting("ArtificialIntelligence:RateLimiting:AssistantSubmitPermitLimit", "100000");
        foreach (var (index, provider) in new[] { (0, "OpenAI"), (1, "Anthropic") })
        {
            var prefix = $"ArtificialIntelligence:Assistant:ModelProfiles:{index}:";
            builder.UseSetting(prefix + "Id", "home");
            builder.UseSetting(prefix + "Version", "1");
            builder.UseSetting(prefix + "Provider", provider);
            builder.UseSetting(prefix + "Model", "fictional-model");
            builder.UseSetting(prefix + "ContextBytes", "131072");
            builder.UseSetting(prefix + "SupportsTools", "true");
            builder.UseSetting(prefix + "SupportsTerminalAnswer", "true");
        }
    }
    public void ConfigureServices(ApiFactory factory, IServiceCollection services)
    {
        services.RemoveAll<ILlmClient>();
        services.AddSingleton<ILlmClient, FictionalLlmClient>();
        services.RemoveAll<IBusinessPlacesClient>();
        services.AddSingleton<IBusinessPlacesClient, FictionalPlacesClient>();
        services.AddHttpClient(ArtificialIntelligenceInfrastructureRegistration.GooglePlacesHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new NoNetworkHandler());
        services.AddHttpClient(ArtificialIntelligenceInfrastructureRegistration.OpenAiHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new NoNetworkHandler());
        services.AddHttpClient(ArtificialIntelligenceInfrastructureRegistration.AnthropicHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new NoNetworkHandler());
        services.AddHttpClient(AiAssistantProvidersRegistration.OpenAiConversationHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new NoNetworkHandler());
        services.AddHttpClient(AiAssistantProvidersRegistration.AnthropicConversationHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new NoNetworkHandler());
    }
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Automated tests never call a billable AI provider.");
    }
    private sealed class FictionalLlmClient : ILlmClient
    {
        public Task<Result<IReadOnlyList<AiModelResponse>>> ListModelsAsync(LlmModelsRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<AiModelResponse>>([new("fictional-model", "Fictional model")]));
        public Task<Result<LlmTestResponse>> TestAsync(LlmTestRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new LlmTestResponse(4, 1)));
    }
    private sealed class FictionalPlacesClient : IBusinessPlacesClient
    {
        public Task<Result<BusinessPlacesResponse>> SearchAsync(string input, string languageCode, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new BusinessPlacesResponse([new("ChIJ_fictional", "Fictional clinic, Example City")])));
    }
}
