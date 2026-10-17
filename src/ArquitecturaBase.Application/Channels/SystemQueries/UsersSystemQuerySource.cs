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

internal sealed class UsersSystemQuerySource(IUserReader users, IPermissionService permissions, TimeProvider timeProvider) : IAssistantCapabilitySource
{
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } =
        [new("users.summary", 1, AssistantCapabilityKind.Read, "Count all active and inactive nondeleted accounts. No personal data.",
            "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}", SystemQueryCapabilityCodec.OutputSchemaJson, new([Permissions.Users.Read], []), new(1024, 16384, 50, 5))];

    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Capability is not { Id: "users.summary", Version: 1 } ||
            SystemQueryCapabilityCodec.ParseArguments<Arguments>(request.ArgumentsJson).IsFailure)
            return SystemQueryErrors.InvalidArguments;
        if (await users.FindByIdAsync(request.Context.ActorUserId, cancellationToken) is not { IsActive: true }
            || !await permissions.HasPermissionAsync(request.Context.ActorUserId, Permissions.Users.Read, cancellationToken))
            return SystemQueryErrors.Forbidden;
        var summary = await users.FindSummaryAsync(cancellationToken);
        return SystemQueryCapabilityCodec.Response("complete", new SystemQueryEvidence(Guid.CreateVersion7(), "users.summary", 1,
            timeProvider.GetUtcNow().UtcDateTime, null, "users", [Permissions.Users.Read],
            [new(null, [new(SystemHelpTexts.Get("ActiveAccounts", request.Context.Culture), summary.ActiveCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new(SystemHelpTexts.Get("InactiveAccounts", request.Context.Culture), summary.InactiveCount.ToString(System.Globalization.CultureInfo.InvariantCulture))])]));
    }

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
        SystemQueryCapabilityCodec.ParseOutput(capability, outputJson, Definitions, "complete");

    private sealed record Arguments;
}
