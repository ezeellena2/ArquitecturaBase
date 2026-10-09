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
    IReadOnlyCollection<AiAssistantModelProfile> profiles, AiAssistantLimits limits, ICurrentUser currentUser)
{
    public Guid? CurrentUserId => currentUser.UserId;
    public async Task<Result<UserAccount>> AuthorizeAsync(Guid userId, CancellationToken ct)
    {
        var account = await users.FindByIdAsync(userId, ct);
        if (account is null || !account.IsActive || !await permissions.HasPermissionAsync(userId, ArtificialIntelligencePermissions.AssistantUse, ct))
            return AiAssistantErrors.Forbidden;
        return account;
    }

    public async Task<bool> CanReadAsync(Guid userId, IEnumerable<string> requiredPermissions, CancellationToken ct)
    {
        var current = await permissions.GetPermissionsAsync(userId, ct);
        return requiredPermissions.All(current.Contains);
    }

    public string Availability(AiConnectionSnapshot connection)
    {
        if (connection.ProtectedApiKey is null || string.IsNullOrWhiteSpace(connection.Model)) return "notConfigured";
        if (!connection.Enabled) return "paused";
        return profiles.Any(p => p.Provider == connection.Provider && p.Model == connection.Model && p.SupportsTools &&
            p.SupportsTerminalAnswer && p.ContextBytes >= limits.ContextBytes) ? "available" : "modelNotQualified";
    }
}
