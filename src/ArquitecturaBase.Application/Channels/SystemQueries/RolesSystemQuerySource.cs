using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;

namespace ArquitecturaBase.Application.Channels.SystemQueries;

internal sealed class RolesSystemQuerySource(IRoleReader roles, IUserReader users, IPermissionService permissions,
    IPermissionCatalog catalog, TimeProvider timeProvider) : IAssistantCapabilitySource
{
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } =
    [
        new("roles.list", 1, AssistantCapabilityKind.Read, "List the role catalog, bounded to 50 roles. User counts exclude deleted accounts.",
            "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}", SystemQueryCapabilityCodec.OutputSchemaJson, new([Permissions.Roles.Read], []), new(1024, 16384, 50, 5)),
        new("roles.permissions", 1, AssistantCapabilityKind.Read, "Read one role by exact name or ID. Specify one selector; unknown names need clarification.",
            "{\"type\":\"object\",\"properties\":{\"roleId\":{\"type\":[\"string\",\"null\"]},\"roleName\":{\"type\":[\"string\",\"null\"]}},\"required\":[\"roleId\",\"roleName\"],\"additionalProperties\":false}", SystemQueryCapabilityCodec.OutputSchemaJson, new([Permissions.Roles.Read], []), new(1024, 16384, 50, 5)),
    ];

    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Capability.Version != 1 || request.Capability.Id is not ("roles.list" or "roles.permissions")) return SystemQueryErrors.InvalidArguments;
        RoleArguments? selector = null;
        if (request.Capability.Id == "roles.list")
        {
            if (SystemQueryCapabilityCodec.ParseArguments<EmptyArguments>(request.ArgumentsJson).IsFailure) return SystemQueryErrors.InvalidArguments;
        }
        else
        {
            var parsed = SystemQueryCapabilityCodec.ParseArguments<RoleArguments>(request.ArgumentsJson, "roleId", "roleName");
            if (parsed.IsFailure) return parsed.Error;
            selector = parsed.Value;
            if (selector.RoleId == Guid.Empty || selector.RoleName is { } name && (string.IsNullOrWhiteSpace(name) || name.Length > 256) ||
                (selector.RoleId is null) == (selector.RoleName is null)) return SystemQueryErrors.InvalidArguments;
        }
        if (await users.FindByIdAsync(request.Context.ActorUserId, cancellationToken) is not { IsActive: true }
            || !await permissions.HasPermissionAsync(request.Context.ActorUserId, Permissions.Roles.Read, cancellationToken)) return SystemQueryErrors.Forbidden;
        if (selector is null)
        {
            var page = await roles.ListRolesAsync(new ListRolesRequest { Page = 1, PageSize = 50, Sort = "name" }, cancellationToken);
            if (page.TotalCount > 50) return SystemQueryCapabilityCodec.Response("truncated", null);
            return Evidence(request, [.. page.Items.Select(role => Row(role, request.Context.Culture, includePermissions: false))]);
        }
        var selected = selector.RoleId is { } id ? await roles.FindByIdAsync(id, cancellationToken)
            : await roles.FindByNameAsync(selector.RoleName!, cancellationToken);
        return selected is null ? SystemQueryCapabilityCodec.Response("notFound", null)
            : Evidence(request, [Row(selected, request.Context.Culture, includePermissions: true)]);
    }

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
        SystemQueryCapabilityCodec.ParseOutput(capability, outputJson, Definitions,
            capability.Id == "roles.list" ? ["complete", "truncated"] : ["complete", "notFound"]);

    private AssistantCapabilityResponse Evidence(AssistantCapabilityRequest request, IReadOnlyList<SystemQueryRow> rows) =>
        SystemQueryCapabilityCodec.Response("complete", new(Guid.CreateVersion7(), request.Capability.Id, 1, timeProvider.GetUtcNow().UtcDateTime, null,
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

    private sealed record EmptyArguments;
    private sealed record RoleArguments(Guid? RoleId, string? RoleName);
}
