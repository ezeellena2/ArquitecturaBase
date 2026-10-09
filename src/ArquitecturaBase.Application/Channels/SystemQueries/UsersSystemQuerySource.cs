using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;

namespace ArquitecturaBase.Application.Channels.SystemQueries;

internal sealed class UsersSystemQuerySource(IUserReader users, IPermissionService permissions, TimeProvider timeProvider) : ISystemQuerySource
{
    public IReadOnlyList<SystemQueryDefinition> Definitions { get; } =
        [new("users.summary", 1, "Count all active and inactive nondeleted accounts. No personal data.",
            "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}", [Permissions.Users.Read])];

    public async Task<Result<SystemQueryResponse>> ExecuteAsync(SystemQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ToolId != "users.summary" || request.RoleId is not null || request.RoleName is not null || request.HelpTopic is not null)
            return SystemQueryErrors.InvalidArguments;
        if (await users.FindByIdAsync(request.ActorUserId, cancellationToken) is not { IsActive: true }
            || !await permissions.HasPermissionAsync(request.ActorUserId, Permissions.Users.Read, cancellationToken))
            return SystemQueryErrors.Forbidden;
        var summary = await users.FindSummaryAsync(cancellationToken);
        return new SystemQueryResponse("complete", new SystemQueryEvidence(Guid.CreateVersion7(), "users.summary", 1,
            timeProvider.GetUtcNow().UtcDateTime, null, "users", [Permissions.Users.Read],
            [new(null, [new(SystemHelpTexts.Get("ActiveAccounts", request.Culture), summary.ActiveCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new(SystemHelpTexts.Get("InactiveAccounts", request.Culture), summary.InactiveCount.ToString(System.Globalization.CultureInfo.InvariantCulture))])]));
    }
}
