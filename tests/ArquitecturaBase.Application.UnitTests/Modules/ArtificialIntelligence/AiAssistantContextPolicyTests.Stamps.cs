using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed partial class AiAssistantContextPolicyTests
{
    [Fact]
    public async Task Preparing_a_turn_uses_its_accepted_profile_version_when_a_newer_version_is_also_registered()
    {
        var host = new Host("en", [1, 2]);
        var captured = CapturedHomeProfile(host);
        host.Reader.Row = host.Reader.Row with { ExecutionProfileJson = JsonSerializer.Serialize(captured, AiAssistantResponsePolicy.JsonOptions) };
        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.ExecutionProfile);
        Assert.Equal(captured.ModelProfileId, result.Value.ExecutionProfile.ModelProfileId);
        Assert.Equal(captured.InstructionsId, result.Value.ExecutionProfile.InstructionsId);
        Assert.Equal(captured.InstructionsVersion, result.Value.ExecutionProfile.InstructionsVersion);
        Assert.Equal(captured.ChannelContext.ChannelId, result.Value.ExecutionProfile.ChannelContext.ChannelId);
        Assert.Equal(captured.ChannelContext.ActorUserId, result.Value.ExecutionProfile.ChannelContext.ActorUserId);
        Assert.Equal(captured.ChannelContext.SubjectUserId, result.Value.ExecutionProfile.ChannelContext.SubjectUserId);
        Assert.Equal(captured.ChannelContext.RequiredPermissions.Actor, result.Value.ExecutionProfile.ChannelContext.RequiredPermissions.Actor);
        Assert.Equal(1, result.Value.ExecutionProfile.ModelProfileVersion);
        Assert.StartsWith("Assistant policy version 1.", result.Value.ProviderRequest.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparing_a_turn_cannot_silently_substitute_a_newer_profile_for_the_accepted_version()
    {
        var host = new Host("en", [2]);
        host.Reader.Row = host.Reader.Row with { ExecutionProfileJson = JsonSerializer.Serialize(CapturedHomeProfile(host), AiAssistantResponsePolicy.JsonOptions) };
        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(0, host.Protector.Decryptions);
    }

    [Fact]
    public async Task A_sealed_context_for_another_actor_is_rejected_before_reading_protected_content()
    {
        var host = new Host("en");
        var captured = CapturedHomeProfile(host);
        captured = captured with { ChannelContext = captured.ChannelContext with { ActorUserId = Guid.NewGuid() } };
        host.Reader.Row = host.Reader.Row with { ExecutionProfileJson = JsonSerializer.Serialize(captured, AiAssistantResponsePolicy.JsonOptions) };
        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(0, host.Protector.Decryptions);
    }

    [Fact]
    public async Task An_inactive_explicit_subject_blocks_preparation_without_borrowing_the_actors_activity()
    {
        var host = new Host("en");
        var inactive = host.Users.AddUser("inactive-subject-fiction@example.test", isActive: false);
        var captured = CapturedHomeProfile(host);
        captured = captured with { ChannelContext = captured.ChannelContext with { ChannelId = "fixture-subject", SubjectUserId = inactive.Id } };
        host.Reader.Row = host.Reader.Row with { ChannelId = "fixture-subject", ExecutionProfileJson = JsonSerializer.Serialize(captured, AiAssistantResponsePolicy.JsonOptions) };
        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(0, host.Protector.Decryptions);
    }

    [Theory]
    [InlineData("instructions")]
    [InlineData("capability")]
    [InlineData("terminal")]
    public async Task Preparing_a_turn_rejects_unknown_captured_versions_before_decrypting_content(string component)
    {
        var host = new Host("en");
        var captured = CapturedHomeProfile(host);
        if (component == "instructions") captured = captured with { InstructionsVersion = 2 };
        if (component == "capability") captured = captured with { ChannelContext = captured.ChannelContext with { AllowedCapabilities = [new("users.summary", 99)] } };
        if (component == "terminal") captured = captured with { ChannelContext = captured.ChannelContext with { AllowedTerminals = [new("assistant_answer", 99)] } };
        host.Reader.Row = host.Reader.Row with { ExecutionProfileJson = JsonSerializer.Serialize(captured, AiAssistantResponsePolicy.JsonOptions) };
        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(0, host.Protector.Decryptions);
    }

    [Fact]
    public async Task Neutral_interpretation_uses_the_original_message_time_from_its_seal_instead_of_the_workers_clock()
    {
        var host = new Host("en");
        var occurredAtUtc = new DateTime(2026, 6, 1, 23, 45, 0, DateTimeKind.Utc);
        var captured = CapturedHomeProfile(host) with { InstructionsId = "capabilities-v1", OccurredAtUtc = occurredAtUtc };
        captured = captured with { ChannelContext = captured.ChannelContext with { ChannelId = "fixture-original-time" } };
        host.Reader.Row = host.Reader.Row with { ChannelId = "fixture-original-time", ExecutionProfileJson = JsonSerializer.Serialize(captured, AiAssistantResponsePolicy.JsonOptions) };
        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Contains("2026-06-01T23:45:00.0000000Z", result.Value.ProviderRequest.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("UTC", result.Value.ProviderRequest.SystemPrompt, StringComparison.Ordinal);
    }

    private static AiAssistantExecutionProfileSnapshot CapturedHomeProfile(Host host) =>
        new("home", 1, "home-system", 1, new("home", host.UserId, null, "en", "UTC",
            new([ArtificialIntelligencePermissions.AssistantUse], []), [], [new("assistant_answer", 1)]));
}
