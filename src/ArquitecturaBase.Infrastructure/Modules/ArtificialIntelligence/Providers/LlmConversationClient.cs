using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.Anthropic;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.OpenAI;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers;

internal sealed class LlmConversationClient(OpenAiConversationClient openAi, AnthropicConversationClient anthropic) : ILlmConversationClient
{
    public Task<Result<LlmConversationResponse>> SendAsync(LlmConversationRequest request, CancellationToken cancellationToken) => request.Provider switch
    {
        AiProvider.OpenAI => openAi.SendAsync(request, cancellationToken),
        AiProvider.Anthropic => anthropic.SendAsync(request, cancellationToken),
        _ => Task.FromResult<Result<LlmConversationResponse>>(AiAssistantErrors.Invalid)
    };
}
