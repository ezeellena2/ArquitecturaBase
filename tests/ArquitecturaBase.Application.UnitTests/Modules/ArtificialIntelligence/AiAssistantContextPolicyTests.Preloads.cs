using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// Casos del asistente: the instructions stay the same from turn to turn so the provider can reuse them; the message's time goes at
// the end of the message, preloads are never tools and the second proposal terminal is offered with its own contract.
public sealed partial class AiAssistantContextPolicyTests
{
    private static readonly AssistantCapabilityDefinition PreloadedRead =
        PreparedAssistantCapabilitySource.Definition("fictional.preload", 1, AssistantCapabilityKind.Read, "fictional.read");

    private static Host ChannelHost() =>
        new("es", sources: [new PreparedAssistantCapabilitySource([PreloadedRead])]);

    private static void Seal(Host host, Func<AssistantChannelContext, AssistantChannelContext> change, DateTime? occurredAtUtc = null)
    {
        var captured = CapturedHomeProfile(host) with { InstructionsId = "capabilities-v1", OccurredAtUtc = occurredAtUtc };
        captured = captured with { ChannelContext = change(captured.ChannelContext with { ChannelId = "fixture-preload", Culture = host.Reader.Row.Culture }) };
        host.Reader.Row = host.Reader.Row with { ChannelId = "fixture-preload", ExecutionProfileJson = JsonSerializer.Serialize(captured, AiAssistantResponsePolicy.JsonOptions) };
    }

    [Fact]
    public async Task The_message_time_goes_at_the_end_of_the_current_message_and_the_instructions_stay_the_same()
    {
        var host = ChannelHost();
        var occurredAtUtc = new DateTime(2026, 6, 1, 23, 45, 0, DateTimeKind.Utc);
        Seal(host, context => context, occurredAtUtc);
        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var request = result.Value.ProviderRequest;
        Assert.Equal(AssistantTexts.Get("Prompt.CapabilitiesV1", "es"), request.SystemPrompt);
        var time = AssistantTexts.Get("Prompt.SourceTime", "es").Replace("{0}", "2026-06-01T23:45:00.0000000Z", StringComparison.Ordinal)
            .Replace("{1}", "UTC", StringComparison.Ordinal);
        Assert.StartsWith("Current question", request.Messages[^1].Text, StringComparison.Ordinal);
        Assert.EndsWith(time, request.Messages[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sealed_preload_is_never_a_tool()
    {
        var host = ChannelHost();
        host.Permissions.Permissions[host.UserId] = [ArtificialIntelligencePermissions.AssistantUse, "fictional.read"];
        Seal(host, context => context with { PreloadedCapabilities = [new("fictional.preload", 1)] });
        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(result.Value.ProviderRequest.Tools, tool => tool.Name == "fictional.preload");
    }

    [Fact]
    public async Task A_sealed_preload_the_catalog_no_longer_offers_blocks_the_preparation_before_decrypting()
    {
        var host = ChannelHost();
        // The seal kept no requirement for the preload, and the actor cannot read it any more.
        Seal(host, context => context with { PreloadedCapabilities = [new("fictional.preload", 1)] });
        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);
        Assert.Equal(AiAssistantErrors.ContentBlocked, result.Error);
        Assert.Equal(0, host.Protector.Decryptions);
    }

    [Fact]
    public async Task The_second_proposal_terminal_is_offered_with_a_closed_list_of_one_to_three_ids()
    {
        var host = ChannelHost();
        Seal(host, context => context with { AllowedTerminals = [new("assistant_proposal", 2)] });
        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var terminal = Assert.Single(result.Value.ProviderRequest.Tools, tool => tool.Name == "assistant_proposal");
        Assert.Equal(AssistantTexts.Get("Prompt.TerminalProposalV2Description", "es"), terminal.Description);
        using var schema = JsonDocument.Parse(terminal.ParametersJson);
        var ids = schema.RootElement.GetProperty("properties").GetProperty("proposalIds");
        Assert.Equal((1, 3), (ids.GetProperty("minItems").GetInt32(), ids.GetProperty("maxItems").GetInt32()));
        Assert.Equal(new[] { "proposalIds" }, schema.RootElement.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
    }
}
