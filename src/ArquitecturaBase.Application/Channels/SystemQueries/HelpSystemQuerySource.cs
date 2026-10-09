using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;

namespace ArquitecturaBase.Application.Channels.SystemQueries;

internal sealed class HelpSystemQuerySource(IUserReader users, IPermissionService permissions, TimeProvider timeProvider) : ISystemQuerySource
{
    public IReadOnlyList<SystemQueryDefinition> Definitions { get; } =
    [new("system.help", 1, "Curated product help. Topic: rolePermissions, userRoles, or profile. Links depend on current access.",
        "{\"type\":\"object\",\"properties\":{\"topic\":{\"type\":\"string\",\"enum\":[\"rolePermissions\",\"userRoles\",\"profile\"]}},\"required\":[\"topic\"],\"additionalProperties\":false}", [])];

    public async Task<Result<SystemQueryResponse>> ExecuteAsync(SystemQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ToolId != "system.help" || request.RoleId is not null || request.RoleName is not null
            || request.HelpTopic is not ("rolePermissions" or "userRoles" or "profile")) return SystemQueryErrors.InvalidArguments;
        if (await users.FindByIdAsync(request.ActorUserId, cancellationToken) is not { IsActive: true }) return SystemQueryErrors.Forbidden;
        var (key, destination, needed) = request.HelpTopic switch
        {
            "rolePermissions" => ("RolePermissionsHelp", "roles", Permissions.Roles.Read),
            "userRoles" => ("UserRolesHelp", "users", Permissions.Users.Read),
            _ => ("ProfileHelp", "profile", (string?)null),
        };
        if (needed is not null && !await permissions.HasPermissionAsync(request.ActorUserId, needed, cancellationToken)) destination = null;
        return new SystemQueryResponse("complete", new SystemQueryEvidence(Guid.CreateVersion7(), "system.help", 1,
            timeProvider.GetUtcNow().UtcDateTime, 1, destination, [],
            [new(null, [new(SystemHelpTexts.Get("Help", request.Culture), SystemHelpTexts.Get(key, request.Culture))])]));
    }
}
