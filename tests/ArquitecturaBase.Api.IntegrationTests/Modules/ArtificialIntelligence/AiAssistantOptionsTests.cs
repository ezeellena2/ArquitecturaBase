using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantOptionsTests
{
    [Fact]
    public void An_installation_without_qualified_profiles_starts_with_an_unavailable_assistant()
    {
        var options = new AiAssistantOptions();
        Assert.True(options.IsValid());
        Assert.Empty(options.ModelProfiles);
    }

    [Fact]
    public void Duplicate_and_incomplete_model_profiles_are_rejected_at_startup()
    {
        var qualified = new AiAssistantModelProfile(AiProvider.OpenAI, "fictional-model", 131072, true, true) {Id="fixture-qualified"};
        Assert.False(new AiAssistantOptions { ModelProfiles = [qualified, qualified] }.IsValid());
        Assert.False(new AiAssistantOptions { ModelProfiles = [qualified with { SupportsTools = false }] }.IsValid());
        Assert.False(new AiAssistantOptions { ModelProfiles = [qualified with { SupportsTerminalAnswer = false }] }.IsValid());
        Assert.False(new AiAssistantOptions { ModelProfiles = [qualified with { ContextBytes = 1024 }] }.IsValid());
        Assert.True(new AiAssistantOptions { ModelProfiles = [qualified] }.IsValid());
    }

    [Fact]
    public void A_text_profile_without_an_explicit_identifier_is_not_a_qualified_profile()
    {
        var unnamed=new AiAssistantModelProfile(AiProvider.OpenAI,"fictional-model",131072,true,true);
        Assert.False(new AiAssistantOptions {ModelProfiles=[unnamed]}.IsValid());
    }
    [Fact]
    public void Configuration_cannot_extend_the_total_deadline_or_close_retry_window()
    {
        Assert.False(new AiAssistantOptions { Limits = new AiAssistantLimits { RunningSeconds = 91 } }.IsValid());
        Assert.False(new AiAssistantOptions { CompletionPersistenceSeconds = 6 }.IsValid());
        Assert.False(new AiAssistantOptions { Limits = new AiAssistantLimits { ToolAggregateBytes = 1 } }.IsValid());
    }
}
