using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Auth.SignInWithExternalProvider;

internal sealed class SignInWithExternalProviderCommandHandler(
    IIdentityService identityService,
    ILoginAuditRepository loginAudits,
    AccountCreationPolicy accountCreation,
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
            return Fail(identifier: string.Empty, user: null, ExternalLoginErrors.Failed);
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
                // InviteOnly: la cuenta la tiene que crear un administrador, salvo la del administrador inicial
                // (AccountCreationPolicy). Acá se puede decir con todas las letras, porque la persona ya probó ante el
                // proveedor que la dirección es suya.
                if (!await accountCreation.AllowsNewAccountAsync(email.Value, cancellationToken))
                {
                    return Fail(email.Value.Value, user: null, AccountErrors.NotInvited);
                }

                if (await identityService.IsDeletedEmailAsync(email.Value, cancellationToken))
                {
                    return Fail(email.Value.Value, user: null, AccountErrors.Disabled);
                }

                user = await identityService.CreateAsync(
                    email.Value,
                    phone: null,
                    phoneConfirmed: false,
                    login.DisplayName,
                    UserCultures.FromCurrentRequest(),
                    cancellationToken);
            }
            else if (!user.EmailConfirmed)
            {
                // Lo cargó un administrador y quedó sin verificar: Google ya probó que la dirección es de la persona, que
                // es lo mismo que entrar con el código que llegó ahí (sección 6.1 del spec del ingreso con WhatsApp).
                await identityService.SetEmailAsync(user.Id, email.Value, confirmed: true, cancellationToken);
            }

            await identityService.AddExternalLoginAsync(user.Id, login, cancellationToken);
        }

        var auditIdentifier = AuditIdentifierOf(user, login);

        if (!user.IsActive)
        {
            return Fail(auditIdentifier, user, AccountErrors.Disabled);
        }

        // Igual que el ingreso con código: una cuenta bloqueada no entra por ningún medio.
        if (await identityService.IsLockedOutAsync(user.Id, cancellationToken))
        {
            return Fail(auditIdentifier, user, AccountErrors.LockedOut);
        }

        await identityService.SignInAsync(user.Id, cancellationToken);

        loginAudits.Add(LoginAudit.Success(
            auditIdentifier, user.Id, LoginMethod.Google, requestInfo.IpAddress, requestInfo.UserAgent, UtcNow()));

        return new SignInWithExternalProviderResponse(command.ReturnUrl!);
    }

    /// <summary>
    /// Con qué queda identificado el ingreso en la auditoría: el correo de la cuenta, como hasta ahora. Una cuenta de
    /// solo número con Google vinculado no tiene, y queda con el que mandó Google, que es con el que se presentó.
    /// </summary>
    private static string AuditIdentifierOf(UserAccount user, ExternalLogin login) =>
        user.Email ?? (Email.Create(login.Email) is { IsSuccess: true } googleEmail ? googleEmail.Value.Value : string.Empty);

    private Error Fail(string identifier, UserAccount? user, Error error)
    {
        loginAudits.Add(LoginAudit.Failure(
            identifier, user?.Id, LoginMethod.Google, error.Code, requestInfo.IpAddress, requestInfo.UserAgent, UtcNow()));

        return error;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
