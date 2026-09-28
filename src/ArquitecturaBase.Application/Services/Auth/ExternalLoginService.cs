using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
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
    ISignInService signIn,
    IUserReader users,
    IUserRepository userRepository,
    ILoginAuditRepository loginAudits,
    AccountCreationPolicy accountCreation,
    IRequestInfo requestInfo,
    TimeProvider timeProvider,
    IRequestValidator validator,
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

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => SignInCoreAsync(ct),
            // Cada error de negocio deja su auditoría, y con una cuenta inactiva o bloqueada también el vínculo nuevo y el
            // correo confirmado: el error se confirma. Una excepción igual deshace todo.
            CommitPolicy.OnAnyResult,
            cancellationToken);

        if (result.IsFailure)
        {
            LogFailed(logger, result.Error.Code);
            return result.Error;
        }

        // La cookie de la aplicación sale recién después del commit de la cuenta, el vínculo y la auditoría, como en los
        // otros dos ingresos: SignInAsync lanza adentro de un límite.
        await signIn.SignInAsync(result.Value, cancellationToken);
        LogHandled(logger);

        return new ExternalSignInResponse(request.ReturnUrl!);
    }

    private async Task<Result<Guid>> SignInCoreAsync(CancellationToken cancellationToken)
    {
        var login = await signIn.GetExternalLoginAsync(cancellationToken);
        if (login is null)
        {
            return Fail(identifier: string.Empty, user: null, ExternalLoginErrors.Failed);
        }

        // La cookie externa sirve solo para este callback.
        await signIn.SignOutExternalAsync(cancellationToken);

        var user = await users.FindByExternalLoginAsync(login.Provider, login.ProviderKey, cancellationToken);
        if (user is null)
        {
            var email = Email.Create(login.Email);
            if (!login.EmailVerified || email.IsFailure)
            {
                return Fail(email.IsSuccess ? email.Value.Value : string.Empty, user: null,
                    ExternalLoginErrors.EmailNotVerified);
            }

            await userRepository.LockExternalSignInAsync(
                email.Value, login.Provider, login.ProviderKey, cancellationToken);

            // Otro callback pudo crear el vínculo mientras se esperaba el lock.
            user = await users.FindByExternalLoginAsync(login.Provider, login.ProviderKey, cancellationToken);
            if (user is null)
            {
                user = await users.FindByEmailAsync(email.Value, cancellationToken);
                if (user is null)
                {
                    if (!await accountCreation.AllowsNewAccountAsync(email.Value, cancellationToken))
                    {
                        return Fail(email.Value.Value, user: null, AccountErrors.NotInvited);
                    }

                    if (await users.ExistsDeletedByEmailAsync(email.Value, cancellationToken))
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
        }

        var auditIdentifier = AuditIdentifierOf(user, login);
        if (!user.IsActive)
        {
            return Fail(auditIdentifier, user, AccountErrors.Disabled);
        }

        if (await signIn.IsLockedOutAsync(user.Id, cancellationToken))
        {
            return Fail(auditIdentifier, user, AccountErrors.LockedOut);
        }

        loginAudits.Add(LoginAudit.Success(
            auditIdentifier, user.Id, LoginMethod.Google,
            requestInfo.IpAddress, requestInfo.UserAgent, UtcNow()));

        return user.Id;
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
