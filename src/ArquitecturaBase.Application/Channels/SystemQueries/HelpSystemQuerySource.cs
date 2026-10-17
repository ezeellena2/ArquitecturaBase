using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;

namespace ArquitecturaBase.Application.Channels.SystemQueries;

internal sealed class HelpSystemQuerySource(IUserReader users, IPermissionService permissions, TimeProvider timeProvider) : IAssistantCapabilitySource
{
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } =
    [new("system.help", 1, AssistantCapabilityKind.Read, "Curated product help. Topic: rolePermissions, userRoles, or profile. Links depend on current access.",
        "{\"type\":\"object\",\"properties\":{\"topic\":{\"type\":\"string\",\"enum\":[\"rolePermissions\",\"userRoles\",\"profile\"]}},\"required\":[\"topic\"],\"additionalProperties\":false}", SystemQueryCapabilityCodec.OutputSchemaJson, new([], []), new(1024, 16384, 50, 5))];

    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Capability is not { Id: "system.help", Version: 1 }) return SystemQueryErrors.InvalidArguments;
        var arguments = SystemQueryCapabilityCodec.ParseArguments<Arguments>(request.ArgumentsJson, "topic");
        if (arguments.IsFailure || arguments.Value.Topic is not ("rolePermissions" or "userRoles" or "profile")) return SystemQueryErrors.InvalidArguments;
        if (await users.FindByIdAsync(request.Context.ActorUserId, cancellationToken) is not { IsActive: true }) return SystemQueryErrors.Forbidden;
        var (key, destination, needed) = arguments.Value.Topic switch
        {
            "rolePermissions" => ("RolePermissionsHelp", "roles", Permissions.Roles.Read),
            "userRoles" => ("UserRolesHelp", "users", Permissions.Users.Read),
            _ => ("ProfileHelp", "profile", (string?)null),
        };
        if (needed is not null && !await permissions.HasPermissionAsync(request.Context.ActorUserId, needed, cancellationToken)) destination = null;
        return SystemQueryCapabilityCodec.Response("complete", new SystemQueryEvidence(Guid.CreateVersion7(), "system.help", 1,
            timeProvider.GetUtcNow().UtcDateTime, 1, destination, [],
            [new(null, [new(SystemHelpTexts.Get("Help", request.Context.Culture), SystemHelpTexts.Get(key, request.Context.Culture))])]));
    }

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
        SystemQueryCapabilityCodec.ParseOutput(capability, outputJson, Definitions, "complete");

    private sealed record Arguments(string Topic);
}
