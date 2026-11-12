using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantDocumentOptionsTests
{
    [Fact]
    public void Document_profiles_are_explicit_and_independent_from_the_text_and_audio_qualification()
    {
        var options = new AiAssistantOptions();
        Assert.Empty(options.DocumentModelProfiles);
        Assert.True(options.IsValid());
        options.DocumentModelProfiles = [Profile(), Profile() with { Id = "fixture-pdf", ContentTypes = ["application/pdf"], MaximumPages = 1 }];
        Assert.True(options.IsValid());
        Assert.Empty(options.ModelProfiles);
        Assert.Empty(options.AudioModelProfiles);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("model")]
    [InlineData("modelCharacters")]
    [InlineData("id")]
    [InlineData("longId")]
    [InlineData("version")]
    [InlineData("noTypes")]
    [InlineData("word")]
    [InlineData("repeatedType")]
    [InlineData("noBytes")]
    [InlineData("bytes")]
    [InlineData("noPages")]
    [InlineData("pages")]
    [InlineData("duplicate")]
    public void Unqualified_or_ambiguous_document_profiles_cannot_start(string change)
    {
        var profile = Profile();
        var changed = change switch
        {
            "provider" => profile with { Provider = AiProvider.Anthropic },
            "model" => profile with { Model = "" },
            "modelCharacters" => profile with { Model = "vision model" },
            "id" => profile with { Id = " " },
            "longId" => profile with { Id = new string('a', 81) },
            "version" => profile with { Version = 0 },
            "noTypes" => profile with { ContentTypes = [] },
            "word" => profile with { ContentTypes = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] },
            "repeatedType" => profile with { ContentTypes = ["application/pdf", "application/pdf"] },
            "noBytes" => profile with { MaximumDocumentBytes = 0 },
            "bytes" => profile with { MaximumDocumentBytes = 10 * 1024 * 1024 + 1 },
            "noPages" => profile with { MaximumPages = 0 },
            "pages" => profile with { MaximumPages = 11 },
            _ => profile,
        };
        Assert.False(new AiAssistantOptions { DocumentModelProfiles = change == "duplicate" ? [profile, changed] : [changed] }.IsValid());
    }

    [Fact]
    public void The_document_limits_default_to_the_plan_and_stay_within_the_turn()
    {
        var limits = new AiAssistantLimits();
        Assert.Equal((10, 50, 60, 8192, 20_000), (limits.DocumentActorDailyQuota, limits.DocumentInstallationDailyQuota,
            limits.DocumentTimeoutSeconds, limits.DocumentOutputTokens, limits.MaximumReadingCharacters));
        Assert.True(new AiAssistantOptions().IsValid());
    }

    // One ceiling for the output tokens and one for the reading: the defaults, which the configuration may lower and never raise.
    [Fact]
    public void The_document_ceilings_are_the_defaults_and_the_configuration_may_only_lower_them()
    {
        var limits = new AiAssistantLimits();
        Assert.Equal((8192, 20_000), (AiAssistantLimits.DocumentOutputTokensCeiling, AiAssistantLimits.ReadingCharactersCeiling));
        Assert.Equal((AiAssistantLimits.DocumentOutputTokensCeiling, AiAssistantLimits.ReadingCharactersCeiling),
            (limits.DocumentOutputTokens, limits.MaximumReadingCharacters));
        Assert.True(new AiAssistantOptions { Limits = limits with
            { DocumentOutputTokens = 4096, MaximumReadingCharacters = StructuredAttachmentLimits.MaximumDocumentCharacters } }.IsValid());
    }

    [Theory]
    [InlineData("actorQuota")]
    [InlineData("installationQuota")]
    [InlineData("timeout")]
    [InlineData("timeoutPastTheTurn")]
    [InlineData("outputTokens")]
    [InlineData("noOutputTokens")]
    [InlineData("readingBelowTheDocumentCut")]
    [InlineData("readingAboveTheCeiling")]
    public void Document_limits_outside_their_bounds_cannot_start(string change)
    {
        var limits = new AiAssistantLimits();
        limits = change switch
        {
            "actorQuota" => limits with { DocumentActorDailyQuota = 0 },
            "installationQuota" => limits with { DocumentInstallationDailyQuota = limits.DocumentActorDailyQuota - 1 },
            "timeout" => limits with { DocumentTimeoutSeconds = 0 },
            "timeoutPastTheTurn" => limits with { DocumentTimeoutSeconds = limits.RunningSeconds },
            "outputTokens" => limits with { DocumentOutputTokens = AiAssistantLimits.DocumentOutputTokensCeiling + 1 },
            "noOutputTokens" => limits with { DocumentOutputTokens = 0 },
            "readingBelowTheDocumentCut" => limits with { MaximumReadingCharacters = StructuredAttachmentLimits.MaximumDocumentCharacters - 1 },
            _ => limits with { MaximumReadingCharacters = AiAssistantLimits.ReadingCharactersCeiling + 1 },
        };
        Assert.False(new AiAssistantOptions { Limits = limits }.IsValid());
    }

    private static AiAssistantDocumentModelProfile Profile() => new(AiProvider.OpenAI, "vision-model", "fixture-document", 1,
        ["application/pdf", "image/jpeg", "image/png"], 10 * 1024 * 1024, 10) { SupportsReasoningEffort = true };
}
