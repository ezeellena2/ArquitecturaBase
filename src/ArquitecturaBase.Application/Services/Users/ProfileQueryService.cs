using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// La lectura del perfil de la sesión. Va aparte de <see cref="ProfileService"/> porque juntas suman más de 8
/// dependencias: la fecha del último ingreso y los permisos solo los pide esta. No abre límite.
/// </summary>
internal sealed class ProfileQueryService(
    ICurrentUser currentUser,
    IUserReader users,
    IPermissionService permissionService,
    ILoginAuditRepository loginAudits,
    IPhoneNumberParser phoneNumbers,
    ILogger<ProfileQueryService> logger) : IProfileQueryService
{
    public Task<Result<CurrentUserResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<CurrentUserResponse>>(logger, "GetProfile", async () =>
        {
            var user = currentUser.UserId is { } userId
                ? await users.FindByIdAsync(userId, cancellationToken)
                : null;

            if (user is null)
            {
                return UserErrors.NotFound;
            }

            var roles = await users.ListRoleNamesForUserAsync(user.Id, cancellationToken);
            var permissions = await permissionService.GetPermissionsAsync(user.Id, cancellationToken);

            // Sin número, Create falla y los dos quedan en null, igual que el número.
            var phone = PhoneNumber.Create(user.PhoneNumber);

            return new CurrentUserResponse(
                user.Id,
                user.Email,
                user.EmailConfirmed,
                user.PhoneNumber,
                phone.IsSuccess ? phoneNumbers.FormatInternational(phone.Value) : null,
                phone.IsSuccess ? phoneNumbers.Mask(phone.Value) : null,
                user.PhoneNumberConfirmed,
                await users.ExistsExternalLoginAsync(user.Id, ExternalLoginProviders.Google, cancellationToken),
                user.DisplayName,
                user.Culture,
                user.TimeZoneId,
                [.. roles.Order(StringComparer.Ordinal)],
                [.. permissions.Order(StringComparer.Ordinal)],
                await loginAudits.FindLastSuccessAtUtcAsync(user.Id, cancellationToken));
        });
}
