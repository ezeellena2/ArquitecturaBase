using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantAccessGuard(IUserReader users, IPermissionService permissions,
    IReadOnlyCollection<AiAssistantModelProfile> profiles, AiAssistantLimits limits, ICurrentUser currentUser, AiAssistantCapabilityCatalogPolicy catalog, IReadOnlyCollection<AiAssistantAudioModelProfile> audioProfiles,
    IPrivateFileStore files)
{
    // IA4: the browser records WebM or MP4; Home offers the microphone only when this connection hears a whole recording and
    // there is private storage to keep it.
    public bool HearsHomeAudio(AiConnectionRow connection) => files.IsAvailable && connection.Enabled && connection.ProtectedApiKey is not null
        && audioProfiles.Any(p => p.Provider == connection.Provider && p.MaximumAudioBytes >= SubmitAiAssistantAudioRequest.MaximumBytes
            && p.ContentTypes.Any(type => type is "audio/webm" or "audio/mp4"));

    public Result<AiAssistantExecutionProfileSnapshot> CaptureAudioProfile(AiConnectionRow connection,
        AssistantChannelContext channel, AssistantAudioInput audio, DateTime occurredAtUtc)
    {
        if (!connection.Enabled || connection.ProtectedApiKey is null || connection.Provider != AiProvider.OpenAI) return AiAssistantErrors.Unavailable;
        var profile = audioProfiles.Where(p => p.Provider == connection.Provider && p.MaximumAudioBytes >= audio.Size &&
            p.ContentTypes.Contains(AiAssistantAudioPolicy.MediaType(audio), StringComparer.Ordinal)).OrderByDescending(p => p.Version)
            .ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
        return profile is null ? AiAssistantErrors.Unavailable : new AiAssistantExecutionProfileSnapshot(profile.Id, profile.Version,
            "audio-none-v1", 1, channel) { Purpose = AiAssistantPurpose.NormalizeAudio, OccurredAtUtc = occurredAtUtc };
    }

    public string? Model(AiAssistantExecutionProfileSnapshot profile, AiConnectionRow connection) =>
        profile.Purpose == AiAssistantPurpose.NormalizeAudio ? AudioProfile(profile, connection)?.Model : connection.Model;

    public AiAssistantAudioModelProfile? AudioProfile(AiAssistantExecutionProfileSnapshot profile, AiConnectionRow connection) =>
        audioProfiles.SingleOrDefault(p => p.Id == profile.ModelProfileId && p.Version == profile.ModelProfileVersion && p.Provider == connection.Provider);

    public Guid? CurrentUserId => currentUser.UserId;
    public async Task<Result<UserAccount>> AuthorizeAsync(Guid userId, CancellationToken ct)
    {
        var account = await users.FindByIdAsync(userId, ct);
        if (account is null || !account.IsActive || !await permissions.HasPermissionAsync(userId, ArtificialIntelligencePermissions.AssistantUse, ct))
            return AiAssistantErrors.Forbidden;
        return account;
    }

    public async Task<Result<UserAccount>> AuthorizeAsync(AssistantChannelContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var actor = await users.FindByIdAsync(context.ActorUserId, ct);
        if (actor is not { IsActive: true } || !await CanReadAsync(context.ActorUserId, context.RequiredPermissions.Actor, ct))
            return AiAssistantErrors.Forbidden;
        if (context.SubjectUserId is { } subjectId)
        {
            if (await users.FindByIdAsync(subjectId, ct) is not { IsActive: true } ||
                !await CanReadAsync(subjectId, context.RequiredPermissions.Subject, ct)) return AiAssistantErrors.Forbidden;
        }
        else if (context.RequiredPermissions.Subject.Count != 0) return AiAssistantErrors.Forbidden;
        return actor;
    }

    public async Task<Result<UserAccount>> AuthorizeCapturedAsync(AssistantChannelContext context, CancellationToken ct)
    {
        if (context.ChannelId == "home")
        {
            var home = await AuthorizeAsync(context.ActorUserId, ct);
            if (home.IsFailure) return home.Error;
            var content = await AuthorizeAsync(context, ct);
            return content.IsFailure ? AiAssistantErrors.ContentBlocked : content;
        }
        return await AuthorizeAsync(context, ct);
    }

    public async Task<bool> CanReadAsync(Guid userId, IEnumerable<string> requiredPermissions, CancellationToken ct)
    {
        var current = await permissions.GetPermissionsAsync(userId, ct);
        return requiredPermissions.All(current.Contains);
    }

    public AssistantChannelContext HomeContext(Guid actorUserId, string culture, string timeZoneId) =>
        AiAssistantChannelPolicy.Home(actorUserId, culture, timeZoneId, catalog.OfferedInHome);

    public Task<Result<AiAssistantExecutionProfileSnapshot>> CaptureHomeProfileAsync(AiConnectionRow connection,
        Guid actorUserId, string culture, string timeZoneId, CancellationToken ct) =>
        CaptureProfileAsync(connection, HomeContext(actorUserId, culture, timeZoneId), "home-system", AiAssistantChannelPolicy.HomeInstructionsVersion, null, ct);

    public Task<Result<AiAssistantExecutionProfileSnapshot>> CaptureOperationProfileAsync(AiConnectionRow connection,
        AssistantChannelContext channel, DateTime occurredAtUtc, CancellationToken ct) =>
        CaptureProfileAsync(connection, channel, "capabilities-v1", 1, occurredAtUtc, ct);

    private async Task<Result<AiAssistantExecutionProfileSnapshot>> CaptureProfileAsync(AiConnectionRow connection,
        AssistantChannelContext channel, string instructionsId, int instructionsVersion, DateTime? occurredAtUtc, CancellationToken ct)
    {
        var profile = profiles.Where(p => p.Provider == connection.Provider && p.Model == connection.Model && p.SupportsTools &&
            p.SupportsTerminalAnswer && p.ContextBytes >= limits.ContextBytes).OrderByDescending(p => p.Version).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
        if (profile is null) return AiAssistantErrors.Unavailable;
        var offered = await catalog.BuildAsync(channel, ct);
        if (offered.IsFailure) return offered.Error;
        channel = channel with { AllowedCapabilities = offered.Value.Capabilities.Select(d => new AssistantCapabilityReference(d.Id, d.Version)).ToArray(),
            RequiredPermissions = offered.Value.RequiredPermissions };
        return new AiAssistantExecutionProfileSnapshot(profile.Id, profile.Version, instructionsId, instructionsVersion, channel) { OccurredAtUtc = occurredAtUtc };
    }

    public bool IsProfileQualified(AiAssistantExecutionProfileSnapshot snapshot, AiConnectionRow connection) =>
        snapshot.Purpose == AiAssistantPurpose.NormalizeAudio
            ? snapshot.InstructionsId == "audio-none-v1" && snapshot.InstructionsVersion == 1 && connection.Provider == AiProvider.OpenAI && AudioProfile(snapshot, connection) is not null
            : AiAssistantChannelPolicy.KnownInstructions(snapshot.InstructionsId, snapshot.InstructionsVersion) &&
        profiles.Any(p => p.Id == snapshot.ModelProfileId && p.Version == snapshot.ModelProfileVersion && p.Provider == connection.Provider &&
            p.Model == connection.Model && p.SupportsTools && p.SupportsTerminalAnswer && p.ContextBytes >= limits.ContextBytes);

    public string Availability(AiConnectionRow connection)
    {
        if (connection.ProtectedApiKey is null || string.IsNullOrWhiteSpace(connection.Model)) return "notConfigured";
        if (!connection.Enabled) return "paused";
        return profiles.Any(p => p.Provider == connection.Provider && p.Model == connection.Model && p.SupportsTools &&
            p.SupportsTerminalAnswer && p.ContextBytes >= limits.ContextBytes) ? "available" : "modelNotQualified";
    }
}
