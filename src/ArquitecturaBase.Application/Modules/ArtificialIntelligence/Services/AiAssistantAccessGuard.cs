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
    IReadOnlyCollection<AiAssistantModelProfile> profiles, AiAssistantLimits limits, ICurrentUser currentUser, AiAssistantCapabilityCatalog catalog)
{
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

    public async Task<Result<AiAssistantExecutionProfileSnapshot>> CaptureHomeProfileAsync(AiConnectionSnapshot connection,
        Guid actorUserId, string culture, string timeZoneId, CancellationToken ct)
    {
        var profile = profiles.Where(p => p.Provider == connection.Provider && p.Model == connection.Model && p.SupportsTools &&
            p.SupportsTerminalAnswer && p.ContextBytes >= limits.ContextBytes).OrderByDescending(p => p.Version).FirstOrDefault();
        if (profile is null) return AiAssistantErrors.Unavailable;
        var channel = AiAssistantChannelPolicy.Home(actorUserId, culture, timeZoneId);
        var offered = await catalog.BuildAsync(channel, ct);
        if (offered.IsFailure) return offered.Error;
        channel = channel with { AllowedCapabilities = offered.Value.Capabilities.Select(d => new AssistantCapabilityReference(d.Id, d.Version)).ToArray(),
            RequiredPermissions = offered.Value.RequiredPermissions };
        return new AiAssistantExecutionProfileSnapshot(profile.Id, profile.Version, "home-system", 1, channel);
    }

    public bool IsProfileQualified(AiAssistantExecutionProfileSnapshot snapshot, AiConnectionSnapshot connection) =>
        snapshot.InstructionsVersion == 1 && snapshot.InstructionsId is "home-system" or "capabilities-v1" &&
        profiles.Any(p => p.Id == snapshot.ModelProfileId && p.Version == snapshot.ModelProfileVersion && p.Provider == connection.Provider &&
            p.Model == connection.Model && p.SupportsTools && p.SupportsTerminalAnswer && p.ContextBytes >= limits.ContextBytes);

    public string Availability(AiConnectionSnapshot connection)
    {
        if (connection.ProtectedApiKey is null || string.IsNullOrWhiteSpace(connection.Model)) return "notConfigured";
        if (!connection.Enabled) return "paused";
        return profiles.Any(p => p.Provider == connection.Provider && p.Model == connection.Model && p.SupportsTools &&
            p.SupportsTerminalAnswer && p.ContextBytes >= limits.ContextBytes) ? "available" : "modelNotQualified";
    }
}
