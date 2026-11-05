using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal static class AiAssistantChannelPolicy
{
    private static readonly string[] HomeQueries = ["users.summary", "roles.list", "roles.permissions", "system.help", "business.information"];
    public const int HomeInstructionsVersion = 2;
    private static readonly AssistantTerminalReference HomeTerminalV1 = new("assistant_answer", 1);
    private static readonly AssistantTerminalReference HomeTerminal = new("assistant_answer", 2);

    public static bool IsHomeQuery(string capabilityId) => HomeQueries.Contains(capabilityId, StringComparer.Ordinal);

    // IA4: a Home recording is normalized in a channel of its own, by the same person with the Home permission, before the turn.
    public const string HomeAudioChannelId = "home-audio";
    public static AssistantChannelContext HomeAudio(Guid actorUserId, string culture, string timeZoneId) =>
        new(HomeAudioChannelId, actorUserId, null, culture, timeZoneId, new([ArtificialIntelligencePermissions.AssistantUse], []), [], []);

    // Home keeps its core queries and adds only what an owner explicitly offers in Home; the catalog filters both by permission.
    public static AssistantChannelContext Home(Guid actorUserId, string culture, string timeZoneId, IReadOnlyList<AssistantCapabilityReference> offeredInHome) =>
        new("home", actorUserId, null, culture, timeZoneId, new([ArtificialIntelligencePermissions.AssistantUse], []),
            HomeQueries.Select(id => new AssistantCapabilityReference(id, 1))
                .Concat(offeredInHome.Where(reference => !IsHomeQuery(reference.Id)).OrderBy(reference => reference.Id, StringComparer.Ordinal)).ToArray(),
            [HomeTerminal]);

    public static Result<AiAssistantExecutionProfileSnapshot> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16384) return AiAssistantErrors.ContentUnavailable;
        try
        {
            var profile = JsonSerializer.Deserialize<AiAssistantExecutionProfileSnapshot>(json, AiAssistantResponsePolicy.JsonOptions);
            return profile is not null && profile.ChannelContext is not null && Enum.IsDefined(profile.ChannelContext.InteractionMode) && profile.ModelProfileVersion > 0 &&
                Enum.IsDefined(profile.Purpose) &&
                (profile.Purpose == AiAssistantPurpose.NormalizeAudio
                    ? profile.InstructionsId == "audio-none-v1" && profile.InstructionsVersion == 1 && profile.ChannelContext.ChannelId != "home" &&
                        profile.ChannelContext.AllowedCapabilities.Count == 0 && profile.ChannelContext.AllowedTerminals.Count == 0
                    : KnownInstructions(profile.InstructionsId, profile.InstructionsVersion))
                ? profile : AiAssistantErrors.ContentUnavailable;
        }
        catch (JsonException) { return AiAssistantErrors.ContentUnavailable; }
    }

    public static bool KnownInstructions(string instructionsId, int version) =>
        instructionsId == "home-system" && version is 1 or HomeInstructionsVersion || instructionsId == "capabilities-v1" && version == 1;

    public static bool SameScope(AssistantChannelContext requested, AssistantChannelContext captured) =>
        requested.ChannelId == captured.ChannelId && requested.ActorUserId == captured.ActorUserId && requested.SubjectUserId == captured.SubjectUserId &&
        requested.InteractionMode == captured.InteractionMode && requested.SourceRecordId == captured.SourceRecordId &&
        requested.ChannelConversationId == captured.ChannelConversationId &&
        requested.Culture == captured.Culture && requested.TimeZoneId == captured.TimeZoneId &&
        captured.AllowedCapabilities.All(requested.AllowedCapabilities.Contains) &&
        requested.AllowedTerminals.ToHashSet().SetEquals(captured.AllowedTerminals) &&
        requested.RequiredPermissions.Actor.All(captured.RequiredPermissions.Actor.Contains) &&
        requested.RequiredPermissions.Subject.All(captured.RequiredPermissions.Subject.Contains);

    // A continuation keeps the people and the channel conversation; what may be offered can change between messages.
    public static bool SameConversation(AssistantChannelContext requested, AssistantChannelContext captured) =>
        requested.ChannelId == captured.ChannelId && requested.ActorUserId == captured.ActorUserId &&
        requested.SubjectUserId == captured.SubjectUserId && requested.ChannelConversationId == captured.ChannelConversationId;

    public static bool Matches(AiAssistantExecutionProfileSnapshot profile, Guid actor, string channel, string culture, string timeZoneId) =>
        profile.ChannelContext.ActorUserId == actor && profile.ChannelContext.ChannelId == channel &&
        profile.ChannelContext.Culture == culture && profile.ChannelContext.TimeZoneId == timeZoneId &&
        (channel != "home" || profile.InstructionsId == "home-system" && profile.ChannelContext.SubjectUserId is null &&
            profile.ChannelContext.RequiredPermissions.Actor.Contains(ArtificialIntelligencePermissions.AssistantUse, StringComparer.Ordinal) &&
            (profile.InstructionsVersion == 1
                ? profile.ChannelContext.AllowedCapabilities.All(capability => capability.Version == 1 && IsHomeQuery(capability.Id)) &&
                    profile.ChannelContext.AllowedTerminals.SequenceEqual([HomeTerminalV1])
                : profile.InstructionsVersion == HomeInstructionsVersion &&
                    profile.ChannelContext.AllowedCapabilities.All(capability => capability.Version >= 1 && (capability.Version == 1 || !IsHomeQuery(capability.Id))) &&
                    profile.ChannelContext.AllowedTerminals.SequenceEqual([HomeTerminal])));
}
