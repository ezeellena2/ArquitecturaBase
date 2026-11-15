using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// I15: in Inicio the model knows what day it is. The time of the request and the zone of the profile go at the end of the current
// message, as in the channels, without touching the sealed profile or the instructions, so the retries keep their fingerprint.
public sealed partial class AiAssistantContextPolicyTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Home_sends_the_time_of_the_request_and_the_profile_zone_at_the_end_of_the_current_message(string culture)
    {
        const string zone = "America/Argentina/Buenos_Aires";
        var host = new Host(culture);
        var submittedAtUtc = new DateTime(2026, 11, 14, 13, 5, 0, DateTimeKind.Utc);
        var profile = new AiAssistantExecutionProfileSnapshot("home", 1, "home-system", 1,
            new("home", host.UserId, null, culture, zone, new([ArtificialIntelligencePermissions.AssistantUse], []), [], [new("assistant_answer", 1)]));
        var sealedProfile = JsonSerializer.Serialize(profile, AiAssistantResponsePolicy.JsonOptions);
        host.Reader.Row = host.Reader.Row with { SubmittedAtUtc = submittedAtUtc, TimeZoneId = zone, ExecutionProfileJson = sealedProfile };

        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var request = result.Value.ProviderRequest;
        var time = string.Format(CultureInfo.InvariantCulture, AssistantTexts.Prompt("Prompt.SourceTime", culture),
            submittedAtUtc.ToString("O", CultureInfo.InvariantCulture), zone);
        Assert.EndsWith("\n\n" + time, request.Messages[^1].Text, StringComparison.Ordinal);
        Assert.Equal(AssistantTexts.Prompt("Prompt.SystemV1", culture), request.SystemPrompt);
        Assert.Equal(sealedProfile, host.Reader.Row.ExecutionProfileJson);
    }
}
