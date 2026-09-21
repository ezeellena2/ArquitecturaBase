using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Auth.SignInWithExternalProvider;

internal sealed class SignInWithExternalProviderCommandHandler(
    IIdentityService identityService,
    ILoginAuditRepository loginAudits,
    ISystemSettingsReader systemSettings,
    IRequestInfo requestInfo,
    TimeProvider timeProvider)
    : ICommandHandler<SignInWithExternalProviderCommand, SignInWithExternalProviderResponse>
{
    public async Task<Result<SignInWithExternalProviderResponse>> Handle(
        SignInWithExternalProviderCommand command,
        CancellationToken cancellationToken)
    {
        var login = await identityService.GetExternalLoginAsync(cancellationToken);

        if (login is null)
        {
            return Fail(email: string.Empty, user: null, ExternalLoginErrors.Failed);
        }

        // La cookie externa solo sirve para este paso: se cierra pase lo que pase.
        await identityService.SignOutExternalAsync(cancellationToken);

        var user = await identityService.FindByExternalLoginAsync(login.Provider, login.ProviderKey, cancellationToken);

        if (user is null)
        {
            var email = Email.Create(login.Email);

            // Sin email verificado no se vincula ni se crea: alguien podría presentarse con un email ajeno.
            if (!login.EmailVerified || email.IsFailure)
            {
                return Fail(email.IsSuccess ? email.Value.Value : string.Empty, user: null, ExternalLoginErrors.EmailNotVerified);
            }

            user = await identityService.FindByEmailAsync(email.Value, cancellationToken);

            if (user is null)
            {
                // InviteOnly: la cuenta la tiene que crear un administrador. Acá se puede decir con todas las
                // letras, porque la persona ya probó ante el proveedor que la dirección es suya.
                if (await systemSettings.GetRegistrationModeAsync(cancellationToken) is RegistrationMode.InviteOnly)
                {
                    return Fail(email.Value.Value, user: null, AccountErrors.NotInvited);
                }

                user = await identityService.CreateAsync(
                    email.Value, login.DisplayName, UserCultures.FromCurrentRequest(), cancellationToken);
            }

            await identityService.AddExternalLoginAsync(user.Id, login, cancellationToken);
        }

        if (!user.IsActive)
        {
            return Fail(user.Email, user, AccountErrors.Disabled);
        }

        // Igual que el ingreso con código: una cuenta bloqueada no entra por ningún medio.
        if (await identityService.IsLockedOutAsync(user.Id, cancellationToken))
        {
            return Fail(user.Email, user, AccountErrors.LockedOut);
        }

        await identityService.SignInAsync(user.Id, cancellationToken);

        loginAudits.Add(LoginAudit.Success(
            user.Email, user.Id, LoginMethod.Google, requestInfo.IpAddress, requestInfo.UserAgent, UtcNow()));

        return new SignInWithExternalProviderResponse(command.ReturnUrl!);
    }

    private Error Fail(string email, UserAccount? user, Error error)
    {
        loginAudits.Add(LoginAudit.Failure(
            email, user?.Id, LoginMethod.Google, error.Code, requestInfo.IpAddress, requestInfo.UserAgent, UtcNow()));

        return error;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
