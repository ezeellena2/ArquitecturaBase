using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;

namespace ArquitecturaBase.Application.Channels.SystemQueries;

internal sealed class RolesSystemQuerySource(IRoleReader roles, IUserReader users, IPermissionService permissions,
    IPermissionCatalog catalog, TimeProvider timeProvider) : ISystemQuerySource
{
    public IReadOnlyList<SystemQueryDefinition> Definitions { get; } =
    [
        new("roles.list", 1, "List the role catalog, bounded to 50 roles. User counts exclude deleted accounts.",
            "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}", [Permissions.Roles.Read]),
        new("roles.permissions", 1, "Read one role by exact name or ID. Specify one selector; unknown names need clarification.",
            "{\"type\":\"object\",\"properties\":{\"roleId\":{\"type\":[\"string\",\"null\"]},\"roleName\":{\"type\":[\"string\",\"null\"]}},\"required\":[\"roleId\",\"roleName\"],\"additionalProperties\":false}", [Permissions.Roles.Read]),
    ];

    public async Task<Result<SystemQueryResponse>> ExecuteAsync(SystemQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.HelpTopic is not null || request.ToolId is not ("roles.list" or "roles.permissions")) return SystemQueryErrors.InvalidArguments;
        if (await users.FindByIdAsync(request.ActorUserId, cancellationToken) is not { IsActive: true }
            || !await permissions.HasPermissionAsync(request.ActorUserId, Permissions.Roles.Read, cancellationToken)) return SystemQueryErrors.Forbidden;
        if (request.ToolId == "roles.list")
        {
            if (request.RoleId is not null || request.RoleName is not null) return SystemQueryErrors.InvalidArguments;
            var page = await roles.ListRolesAsync(new ListRolesRequest { Page = 1, PageSize = 50, Sort = "name" }, cancellationToken);
            if (page.TotalCount > 50) return new SystemQueryResponse("truncated", null);
            return Evidence(request, [.. page.Items.Select(role => Row(role, request.Culture, includePermissions: false))]);
        }
        if ((request.RoleId is null) == string.IsNullOrWhiteSpace(request.RoleName)) return SystemQueryErrors.InvalidArguments;
        var selected = request.RoleId is { } id ? await roles.FindByIdAsync(id, cancellationToken)
            : await roles.FindByNameAsync(request.RoleName!, cancellationToken);
        return selected is null ? new SystemQueryResponse("notFound", null)
            : Evidence(request, [Row(selected, request.Culture, includePermissions: true)]);
    }

    private SystemQueryResponse Evidence(SystemQueryRequest request, IReadOnlyList<SystemQueryRow> rows) =>
        new("complete", new(Guid.CreateVersion7(), request.ToolId, 1, timeProvider.GetUtcNow().UtcDateTime, null,
            "roles", [Permissions.Roles.Read], rows));

    private SystemQueryRow Row(RoleRow role, string culture, bool includePermissions)
    {
        List<SystemQueryValue> values =
        [new(SystemHelpTexts.Get("RoleName", culture), role.Name), new(SystemHelpTexts.Get("RoleDescription", culture), role.Description ?? ""),
            new(SystemHelpTexts.Get("AssignedAccounts", culture), role.UserCount.ToString(CultureInfo.InvariantCulture))];
        if (includePermissions)
            values.AddRange(role.Permissions.Where(catalog.Contains).Order(StringComparer.Ordinal)
                .Select(code => new SystemQueryValue(code, catalog.Name(code))));
        return new(role.Id, values);
    }
}
