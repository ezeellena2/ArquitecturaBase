using System.Reflection;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantBoundaryTests
{
    [Fact]
    public void HTTP_assistant_contract_does_not_accept_identity_permissions_or_provider_configuration()
    {
        var contract = Assembly.Load("ArquitecturaBase.Api").GetType("ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts.Assistant.SubmitAiAssistantTurnHttpRequest");
        Assert.NotNull(contract);
        Assert.Equal(["ConversationId", "ExpectedConnectionRevision", "ExpectedRevision", "Question", "RequestId"],
            contract.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Execution_does_not_save_or_call_another_service_writer()
    {
        var calls = CallSites.Calls(typeof(IAiAssistantExecutionService).Assembly)
            .Where(call => call.Owner == "ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services.AiAssistantExecutionService").ToArray();
        Assert.NotEmpty(calls);
        Assert.DoesNotContain(calls, call => call.Method == "ExecuteInTransactionAsync"
            || call.DeclaringType == typeof(IAiAssistantDispatchService).FullName
            || call.DeclaringType == typeof(IAiAssistantService).FullName);
        Assert.Contains(calls, call => call.DeclaringType == typeof(ILlmConversationClient).FullName && call.Method == "SendAsync");
    }

    [Fact]
    public void Assistant_HTTP_service_never_dispatches_provider_generation()
    {
        var calls = CallSites.Calls(typeof(IAiAssistantService).Assembly)
            .Where(call => call.Owner == "ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services.AiAssistantService").ToArray();
        Assert.NotEmpty(calls);
        Assert.DoesNotContain(calls, call => call.DeclaringType == typeof(ILlmConversationClient).FullName
            || call.DeclaringType == typeof(IAiAssistantExecutionService).FullName);
    }

    [Fact]
    public void Conversation_and_provider_content_have_redacted_string_representations()
    {
        var submit = new SubmitAiAssistantTurnRequest(Guid.NewGuid(), null, null, 1, "private-question", "es");
        Assert.DoesNotContain("private-question", submit.ToString(), StringComparison.Ordinal);
        var provider = new LlmConversationRequest(default, "model", "private-key", "private-prompt", [], [], [], null, 10, 100, 100);
        Assert.DoesNotContain("private-key", provider.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-prompt", provider.ToString(), StringComparison.Ordinal);
    }
}
