using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Auth;

internal sealed partial class ExternalLoginService(
    IIdentityService identity,
    IUserReader users,
    IUserRepository userRepository,
    ILoginAuditRepository loginAudits,
    AccountCreationPolicy accountCreation,
    IRequestInfo requestInfo,
    TimeProvider timeProvider,
    ServiceRequestValidator<ExternalSignInRequest> validator,
    IUnitOfWork unitOfWork,
    ILogger<ExternalLoginService> logger) : IExternalLoginService
{
    public async Task<Result<ExternalSignInResponse>> SignInAsync(
        ExternalSignInRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger);

        if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogFailed(logger, validationError.Code);
            return validationError;
        }

        var result = await SignInCoreAsync(request, cancellationToken);

        // El comando anterior usaba IPersistChangesOnFailure: auditoría, vínculos y cuenta pueden haberse escrito
        // antes de que el caso de uso devuelva un error. La validación anterior no tiene esos efectos.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (result.IsSuccess)
        {
            LogHandled(logger);
        }
        else
        {
            LogFailed(logger, result.Error.Code);
        }

        return result;
    }

    private async Task<Result<ExternalSignInResponse>> SignInCoreAsync(
        ExternalSignInRequest request,
        CancellationToken cancellationToken)
    {
        var login = await identity.GetExternalLoginAsync(cancellationToken);
        if (login is null)
        {
            return Fail(identifier: string.Empty, user: null, ExternalLoginErrors.Failed);
        }

        // La cookie externa sirve solo para este callback.
        await identity.SignOutExternalAsync(cancellationToken);

        var user = await users.FindByExternalLoginAsync(login.Provider, login.ProviderKey, cancellationToken);
        if (user is null)
        {
            var email = Email.Create(login.Email);
            if (!login.EmailVerified || email.IsFailure)
            {
                return Fail(email.IsSuccess ? email.Value.Value : string.Empty, user: null,
                    ExternalLoginErrors.EmailNotVerified);
            }

            user = await users.FindByEmailAsync(email.Value, cancellationToken);
            if (user is null)
            {
                if (!await accountCreation.AllowsNewAccountAsync(email.Value, cancellationToken))
                {
                    return Fail(email.Value.Value, user: null, AccountErrors.NotInvited);
                }

                if (await users.IsDeletedEmailAsync(email.Value, cancellationToken))
                {
                    return Fail(email.Value.Value, user: null, AccountErrors.Disabled);
                }

                user = await userRepository.CreateAsync(
                    email.Value, phone: null, phoneConfirmed: false, login.DisplayName,
                    UserCultures.FromCurrentRequest(), cancellationToken);
            }
            else if (!user.EmailConfirmed)
            {
                await userRepository.SetEmailAsync(user.Id, email.Value, confirmed: true, cancellationToken);
            }

            await userRepository.AddExternalLoginAsync(user.Id, login, cancellationToken);
        }

        var auditIdentifier = AuditIdentifierOf(user, login);
        if (!user.IsActive)
        {
            return Fail(auditIdentifier, user, AccountErrors.Disabled);
        }

        if (await identity.IsLockedOutAsync(user.Id, cancellationToken))
        {
            return Fail(auditIdentifier, user, AccountErrors.LockedOut);
        }

        await identity.SignInAsync(user.Id, cancellationToken);
        loginAudits.Add(LoginAudit.Success(
            auditIdentifier, user.Id, LoginMethod.Google,
            requestInfo.IpAddress, requestInfo.UserAgent, UtcNow()));

        return new ExternalSignInResponse(request.ReturnUrl!);
    }

    private static string AuditIdentifierOf(UserAccount user, ExternalLogin login) =>
        user.Email ?? (Email.Create(login.Email) is { IsSuccess: true } googleEmail
            ? googleEmail.Value.Value
            : string.Empty);

    private Error Fail(string identifier, UserAccount? user, Error error)
    {
        loginAudits.Add(LoginAudit.Failure(
            identifier, user?.Id, LoginMethod.Google, error.Code,
            requestInfo.IpAddress, requestInfo.UserAgent, UtcNow()));
        return error;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling external sign-in")]
    private static partial void LogHandling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled external sign-in")]
    private static partial void LogHandled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "External sign-in failed with {ErrorCode}")]
    private static partial void LogFailed(ILogger logger, string errorCode);
}
