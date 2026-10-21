using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal static class AiAssistantChannelPolicy
{
    private static readonly string[] HomeQueries = ["users.summary", "roles.list", "roles.permissions", "system.help", "business.information"];
    public static AssistantChannelContext Home(Guid actorUserId, string culture, string timeZoneId) =>
        new("home", actorUserId, null, culture, timeZoneId, new([ArtificialIntelligencePermissions.AssistantUse], []),
            HomeQueries.Select(id => new AssistantCapabilityReference(id, 1)).ToArray(), [new("assistant_answer", 1)]);

    public static Result<AiAssistantExecutionProfileSnapshot> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16384) return AiAssistantErrors.ContentUnavailable;
        try
        {
            var profile = JsonSerializer.Deserialize<AiAssistantExecutionProfileSnapshot>(json, AiAssistantResponsePolicy.JsonOptions);
            return profile is not null && profile.ChannelContext is not null && Enum.IsDefined(profile.ChannelContext.InteractionMode) && profile.ModelProfileVersion > 0 &&
                profile.InstructionsVersion == 1 && Enum.IsDefined(profile.Purpose) &&
                (profile.Purpose == AiAssistantPurpose.NormalizeAudio
                    ? profile.InstructionsId == "audio-none-v1" && profile.ChannelContext.ChannelId != "home" &&
                        profile.ChannelContext.AllowedCapabilities.Count == 0 && profile.ChannelContext.AllowedTerminals.Count == 0
                    : profile.InstructionsId is "home-system" or "capabilities-v1")
                ? profile : AiAssistantErrors.ContentUnavailable;
        }
        catch (JsonException) { return AiAssistantErrors.ContentUnavailable; }
    }

    public static bool SameScope(AssistantChannelContext requested, AssistantChannelContext captured) =>
        requested.ChannelId == captured.ChannelId && requested.ActorUserId == captured.ActorUserId && requested.SubjectUserId == captured.SubjectUserId &&
        requested.InteractionMode == captured.InteractionMode && requested.SourceRecordId == captured.SourceRecordId &&
        requested.Culture == captured.Culture && requested.TimeZoneId == captured.TimeZoneId &&
        captured.AllowedCapabilities.All(requested.AllowedCapabilities.Contains) &&
        requested.AllowedTerminals.ToHashSet().SetEquals(captured.AllowedTerminals) &&
        requested.RequiredPermissions.Actor.All(captured.RequiredPermissions.Actor.Contains) &&
        requested.RequiredPermissions.Subject.All(captured.RequiredPermissions.Subject.Contains);

    public static bool Matches(AiAssistantExecutionProfileSnapshot profile, Guid actor, string channel, string culture, string timeZoneId) =>
        profile.ChannelContext.ActorUserId == actor && profile.ChannelContext.ChannelId == channel &&
        profile.ChannelContext.Culture == culture && profile.ChannelContext.TimeZoneId == timeZoneId &&
        (channel != "home" || profile.InstructionsId == "home-system" && profile.ChannelContext.SubjectUserId is null &&
            profile.ChannelContext.RequiredPermissions.Actor.Contains(ArtificialIntelligencePermissions.AssistantUse, StringComparer.Ordinal) &&
            profile.ChannelContext.AllowedCapabilities.All(capability => capability.Version == 1 && HomeQueries.Contains(capability.Id, StringComparer.Ordinal)) &&
            profile.ChannelContext.AllowedTerminals.SequenceEqual([new AssistantTerminalReference("assistant_answer", 1)]));
}
