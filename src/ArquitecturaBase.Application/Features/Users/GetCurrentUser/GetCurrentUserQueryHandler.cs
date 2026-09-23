using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.GetCurrentUser;

internal sealed class GetCurrentUserQueryHandler(
    ICurrentUser currentUser,
    IIdentityService identityService,
    IPermissionService permissionService,
    ILoginAuditRepository loginAudits)
    : IQueryHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    public async Task<Result<CurrentUserResponse>> Handle(GetCurrentUserQuery query, CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await identityService.FindByIdAsync(userId, cancellationToken)
            : null;

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var roles = await identityService.GetRolesAsync(user.Id, cancellationToken);
        var permissions = await permissionService.GetPermissionsAsync(user.Id, cancellationToken);

        return new CurrentUserResponse(
            user.Id,
            user.Email,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.PhoneNumberConfirmed,
            await identityService.HasExternalLoginAsync(user.Id, ExternalLoginProviders.Google, cancellationToken),
            user.DisplayName,
            user.Culture,
            user.TimeZoneId,
            [.. roles.Order(StringComparer.Ordinal)],
            [.. permissions.Order(StringComparer.Ordinal)],
            await loginAudits.GetLastSuccessAtUtcAsync(user.Id, cancellationToken));
    }
}
