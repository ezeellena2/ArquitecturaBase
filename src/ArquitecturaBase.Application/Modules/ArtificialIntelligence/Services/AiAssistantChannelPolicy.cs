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

    // Stage 3: a Home photo or PDF is read the same way, in a channel of its own, before the turn names the reading.
    public const string HomeDocumentChannelId = "home-document";
    public static AssistantChannelContext HomeDocument(Guid actorUserId, string culture, string timeZoneId) =>
        new(HomeDocumentChannelId, actorUserId, null, culture, timeZoneId, new([ArtificialIntelligencePermissions.AssistantUse], []), [], []);

    // Home keeps its core queries and adds only what an owner explicitly offers in Home; the catalog filters both by permission.
    public static AssistantChannelContext Home(Guid actorUserId, string culture, string timeZoneId, IReadOnlyList<AssistantCapabilityReference> offeredInHome) =>
        new("home", actorUserId, null, culture, timeZoneId, new([ArtificialIntelligencePermissions.AssistantUse], []),
            HomeQueries.Select(id => new AssistantCapabilityReference(id, 1))
                .Concat(offeredInHome.Where(reference => !IsHomeQuery(reference.Id)).OrderBy(reference => reference.Id, StringComparer.Ordinal)).ToArray(),
            [HomeTerminal]);

    public static Result<AiAssistantExecutionProfileSnapshot> Read(string? json) => Read(json, null);

    // I4: a value above the highest this version knows (a purpose, a kind of file or a mode past the last one, or a version of instructions or
    // of a terminal past the highest of its Id) was sealed by a newer one, and is UnknownVersion. A lower one it no longer knows was retired
    // and, like what no version writes (a negative value, empty instructions, a null terminal or capability), is ContentUnavailable, which
    // wins. The capabilities need the catalog (AiAssistantAccessGuard.ReadSeal).
    public static Result<AiAssistantExecutionProfileSnapshot> Read(string? json, Func<AssistantChannelContext, Error?>? capabilities)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16384) return AiAssistantErrors.ContentUnavailable;
        try
        {
            var profile = JsonSerializer.Deserialize<AiAssistantExecutionProfileSnapshot>(json, AiAssistantResponsePolicy.JsonOptions);
            if (profile?.ChannelContext is not { AllowedCapabilities: not null, AllowedTerminals: not null } context || profile.ModelProfileVersion <= 0 ||
                context.AllowedCapabilities.Concat(context.PreloadedCapabilities ?? []).Any(capability => capability is null))
                return AiAssistantErrors.ContentUnavailable;
            Error?[] verdicts = [Classify(context.InteractionMode), Classify(profile.Purpose), Classify(profile.Attachment), ClassifyInstructions(profile),
                .. context.AllowedTerminals.Select(AiAssistantCapabilityCatalogPolicy.ClassifyTerminal), capabilities?.Invoke(context)];
            if (verdicts.Contains(AiAssistantErrors.ContentUnavailable)) return AiAssistantErrors.ContentUnavailable;
            if (verdicts.Contains(AiAssistantErrors.UnknownVersion)) return AiAssistantErrors.UnknownVersion;
            return !IsTechnical(profile.Purpose) || context.ChannelId != "home" && context.AllowedCapabilities.Count == 0 && context.AllowedTerminals.Count == 0
                ? profile : AiAssistantErrors.ContentUnavailable;
        }
        catch (JsonException) { return AiAssistantErrors.ContentUnavailable; }
    }

    private static Error? Classify<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Enum.IsDefined(value) ? null : Comparer<TEnum>.Default.Compare(value, Enum.GetValues<TEnum>().Max()) > 0
            ? AiAssistantErrors.UnknownVersion : AiAssistantErrors.ContentUnavailable;

    // The technical purposes (audio and documents) never offer capabilities, terminals or history, and always open their own conversation.
    public static bool IsTechnical(AiAssistantPurpose purpose) => purpose is AiAssistantPurpose.NormalizeAudio or AiAssistantPurpose.ReadDocument;

    // Every version up to the highest of each instructions Id is still read; a purpose this version does not know decides by itself.
    private static Error? ClassifyInstructions(AiAssistantExecutionProfileSnapshot profile) => profile.Purpose switch
    {
        AiAssistantPurpose.NormalizeAudio => ClassifyInstructions(profile, "audio-none-v1", 1),
        AiAssistantPurpose.ReadDocument => ClassifyInstructions(profile, AiAssistantDocumentPolicy.InstructionsId, AiAssistantDocumentPolicy.InstructionsVersion),
        AiAssistantPurpose.Interpret => ClassifyInstructions(profile, profile.InstructionsId == "home-system" ? "home-system" : "capabilities-v1",
            profile.InstructionsId == "home-system" ? HomeInstructionsVersion : 1),
        _ => null,
    };

    private static Error? ClassifyInstructions(AiAssistantExecutionProfileSnapshot profile, string id, int highest) =>
        profile.InstructionsId != id || profile.InstructionsVersion <= 0 ? AiAssistantErrors.ContentUnavailable
        : profile.InstructionsVersion > highest ? AiAssistantErrors.UnknownVersion : null;

    public static bool KnownInstructions(string instructionsId, int version) =>
        instructionsId == "home-system" && version is 1 or HomeInstructionsVersion || instructionsId == "capabilities-v1" && version == 1;

    // B2: a Home recording or reading belongs to its person and its request; a language or zone changed while it runs does not lose it.
    private static bool IsHomeTechnical(string channelId) => channelId is HomeAudioChannelId or HomeDocumentChannelId;

    public static bool SameScope(AssistantChannelContext requested, AssistantChannelContext captured) =>
        requested.ChannelId == captured.ChannelId && requested.ActorUserId == captured.ActorUserId && requested.SubjectUserId == captured.SubjectUserId &&
        requested.InteractionMode == captured.InteractionMode && requested.SourceRecordId == captured.SourceRecordId &&
        requested.FocusDefinitionId == captured.FocusDefinitionId &&
        requested.ChannelConversationId == captured.ChannelConversationId &&
        (IsHomeTechnical(captured.ChannelId) || requested.Culture == captured.Culture && requested.TimeZoneId == captured.TimeZoneId) &&
        captured.AllowedCapabilities.All(requested.AllowedCapabilities.Contains) &&
        (captured.PreloadedCapabilities ?? []).All((requested.PreloadedCapabilities ?? []).Contains) &&
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
