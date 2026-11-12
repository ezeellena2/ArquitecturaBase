using ArquitecturaBase.Application.Models.Assistant;
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
    IReadOnlyCollection<AiAssistantModelProfile> profiles, AiAssistantLimits limits, ICurrentUser currentUser, AiAssistantCapabilityCatalogPolicy catalog,
    AiAssistantTechnicalProfilePolicy technical)
{
    public bool HearsHomeAudio(AiConnectionRow connection) => technical.HearsHomeAudio(connection);

    public Result<AiAssistantExecutionProfileSnapshot> CaptureAudioProfile(AiConnectionRow connection,
        AssistantChannelContext channel, AssistantAudioInput audio, DateTime occurredAtUtc) => technical.CaptureAudio(connection, channel, audio, occurredAtUtc);

    // Whether this installation reads documents (ReadDocument): Home's documents route and its bootstrap ask it.
    public bool ReadsDocuments(AiConnectionRow connection) => technical.ReadsDocuments(connection);

    public Result<AiAssistantExecutionProfileSnapshot> CaptureDocumentProfile(AiConnectionRow connection,
        AssistantChannelContext channel, AssistantDocumentInput document, DateTime occurredAtUtc) => technical.CaptureDocument(connection, channel, document, occurredAtUtc);

    // A technical purpose takes the model of its sealed profile; an interpretation, the connection's.
    public string? Model(AiAssistantExecutionProfileSnapshot profile, AiConnectionRow connection) => profile.Purpose switch
    {
        AiAssistantPurpose.NormalizeAudio => AudioProfile(profile, connection)?.Model,
        AiAssistantPurpose.ReadDocument => DocumentProfile(profile, connection)?.Model,
        _ => connection.Model,
    };

    public AiAssistantAudioModelProfile? AudioProfile(AiAssistantExecutionProfileSnapshot profile, AiConnectionRow connection) =>
        technical.Audio(profile, connection);

    public AiAssistantDocumentModelProfile? DocumentProfile(AiAssistantExecutionProfileSnapshot profile, AiConnectionRow connection) =>
        technical.Document(profile, connection);

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

    // What the turn's file does not admit is not offered; what is offered is sealed in the turn, with the kind of that file.
    public Task<Result<AiAssistantExecutionProfileSnapshot>> CaptureHomeProfileAsync(AiConnectionRow connection,
        Guid actorUserId, string culture, string timeZoneId, AssistantAttachmentKind attachment, CancellationToken ct) =>
        CaptureProfileAsync(connection, HomeContext(actorUserId, culture, timeZoneId), "home-system", AiAssistantChannelPolicy.HomeInstructionsVersion, null, attachment, ct);

    public Task<Result<AiAssistantExecutionProfileSnapshot>> CaptureOperationProfileAsync(AiConnectionRow connection,
        AssistantChannelContext channel, DateTime occurredAtUtc, AssistantAttachmentKind attachment, CancellationToken ct) =>
        CaptureProfileAsync(connection, channel, "capabilities-v1", 1, occurredAtUtc, attachment, ct);

    private async Task<Result<AiAssistantExecutionProfileSnapshot>> CaptureProfileAsync(AiConnectionRow connection, AssistantChannelContext channel,
        string instructionsId, int instructionsVersion, DateTime? occurredAtUtc, AssistantAttachmentKind attachment, CancellationToken ct)
    {
        var profile = profiles.Where(p => p.Provider == connection.Provider && p.Model == connection.Model && p.SupportsTools &&
            p.SupportsTerminalAnswer && p.ContextBytes >= limits.ContextBytes).OrderByDescending(p => p.Version).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
        if (profile is null) return AiAssistantErrors.Unavailable;
        // A capability that drafts text goes only with a profile qualified to draft (ADR 0021), so it neither counts nor reserves otherwise.
        if (!profile.QualifiedToDraft)
            channel = channel with { AllowedCapabilities = channel.AllowedCapabilities.Where(reference => catalog.Find(reference)?.DraftsText != true).ToArray() };
        var offered = await catalog.BuildAsync(channel, attachment, ct);
        if (offered.IsFailure) return offered.Error;
        channel = channel with { AllowedCapabilities = offered.Value.Capabilities.Select(d => new AssistantCapabilityReference(d.Id, d.Version)).ToArray(),
            PreloadedCapabilities = offered.Value.Preloaded.Count == 0 ? null
                : offered.Value.Preloaded.Select(d => new AssistantCapabilityReference(d.Id, d.Version)).ToArray(),
            RequiredPermissions = offered.Value.RequiredPermissions };
        return new AiAssistantExecutionProfileSnapshot(profile.Id, profile.Version, instructionsId, instructionsVersion, channel)
            { OccurredAtUtc = occurredAtUtc, Attachment = attachment };
    }

    public bool IsProfileQualified(AiAssistantExecutionProfileSnapshot snapshot, AiConnectionRow connection) => snapshot.Purpose switch
    {
        AiAssistantPurpose.NormalizeAudio => snapshot.InstructionsId == "audio-none-v1" && snapshot.InstructionsVersion == 1 &&
            connection.Provider == AiProvider.OpenAI && AudioProfile(snapshot, connection) is not null,
        AiAssistantPurpose.ReadDocument => snapshot.InstructionsId == AiAssistantDocumentPolicy.InstructionsId &&
            snapshot.InstructionsVersion == AiAssistantDocumentPolicy.InstructionsVersion && connection.Provider == AiProvider.OpenAI &&
            DocumentProfile(snapshot, connection) is not null,
        _ => AiAssistantChannelPolicy.KnownInstructions(snapshot.InstructionsId, snapshot.InstructionsVersion) &&
            profiles.Any(p => p.Id == snapshot.ModelProfileId && p.Version == snapshot.ModelProfileVersion && p.Provider == connection.Provider &&
                p.Model == connection.Model && p.SupportsTools && p.SupportsTerminalAnswer && p.ContextBytes >= limits.ContextBytes &&
                (p.QualifiedToDraft || !snapshot.ChannelContext.AllowedCapabilities.Any(capability => catalog.Find(capability)?.DraftsText == true))),
    };

    public string Availability(AiConnectionRow connection)
    {
        if (connection.ProtectedApiKey is null || string.IsNullOrWhiteSpace(connection.Model)) return "notConfigured";
        if (!connection.Enabled) return "paused";
        return profiles.Any(p => p.Provider == connection.Provider && p.Model == connection.Model && p.SupportsTools &&
            p.SupportsTerminalAnswer && p.ContextBytes >= limits.ContextBytes) ? "available" : "modelNotQualified";
    }
}
