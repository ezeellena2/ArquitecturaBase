using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Users;

internal sealed partial class ProfileService(
    ICurrentUser currentUser,
    IUserReader users,
    IPermissionService permissionService,
    ILoginAuditRepository loginAudits,
    IPhoneNumberParser phoneNumbers,
    ILogger<ProfileService> logger) : IProfileService
{
    private const string RequestName = "GetCurrentUserQuery";

    public async Task<Result<CurrentUserResponse>> GetAsync(CancellationToken cancellationToken)
    {
        LogHandling(logger, RequestName);

        var user = currentUser.UserId is { } userId
            ? await users.FindByIdAsync(userId, cancellationToken)
            : null;

        if (user is null)
        {
            LogFailed(logger, RequestName, UserErrors.NotFoundCode);
            return UserErrors.NotFound;
        }

        var roles = await users.ListRoleNamesForUserAsync(user.Id, cancellationToken);
        var permissions = await permissionService.GetPermissionsAsync(user.Id, cancellationToken);

        // Sin número, Create falla y los dos quedan en null, igual que el número.
        var phone = PhoneNumber.Create(user.PhoneNumber);

        var response = new CurrentUserResponse(
            user.Id,
            user.Email,
            user.EmailConfirmed,
            user.PhoneNumber,
            phone.IsSuccess ? phoneNumbers.FormatInternational(phone.Value) : null,
            phone.IsSuccess ? phoneNumbers.Mask(phone.Value) : null,
            user.PhoneNumberConfirmed,
            await users.HasExternalLoginAsync(user.Id, ExternalLoginProviders.Google, cancellationToken),
            user.DisplayName,
            user.Culture,
            user.TimeZoneId,
            [.. roles.Order(StringComparer.Ordinal)],
            [.. permissions.Order(StringComparer.Ordinal)],
            await loginAudits.GetLastSuccessAtUtcAsync(user.Id, cancellationToken));

        LogHandled(logger, RequestName);
        return response;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {RequestName}")]
    private static partial void LogHandling(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {RequestName}")]
    private static partial void LogHandled(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{RequestName} failed with {ErrorCode}")]
    private static partial void LogFailed(ILogger logger, string requestName, string errorCode);
}
