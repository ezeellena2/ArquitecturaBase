using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantAudioOptionsTests
{
    [Fact]
    public void STT_profiles_are_explicit_and_independent_from_tools_qualification()
    {
        var options = new AiAssistantOptions();
        Assert.Empty(options.AudioModelProfiles);
        options.AudioModelProfiles = [Profile()];
        Assert.True(options.IsValid());
        Assert.Empty(options.ModelProfiles);
    }
    [Theory]
    [InlineData("provider")]
    [InlineData("model")]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("format")]
    [InlineData("bytes")]
    [InlineData("duplicate")]
    public void Unqualified_or_ambiguous_STT_profiles_cannot_start(string change)
    {
        var profile = Profile();
        var changed = change switch {
            "provider" => profile with { Provider = AiProvider.Anthropic },
            "model" => profile with { Model = "" },
            "id" => profile with { Id = "" },
            "version" => profile with { Version = 0 },
            "format" => profile with { ContentTypes = ["audio/unsupported"] },
            "bytes" => profile with { MaximumAudioBytes = 25000001 },
            _ => profile,
        };
        Assert.False(new AiAssistantOptions { AudioModelProfiles = change == "duplicate" ? [profile, changed] : [changed] }.IsValid());
    }
    private static AiAssistantAudioModelProfile Profile() => new(AiProvider.OpenAI, "audio-model", "fixture-audio", 2, ["audio/ogg"], 25000000);
}
