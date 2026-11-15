using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Auth;

internal sealed class ExternalLoginService(
    ISignInService signIn,
    IUserReader users,
    IUserRepository userRepository,
    LoginAuditRecorder audits,
    AccountCreationPolicy accountCreation,
    IRequestValidator validator,
    IUnitOfWork unitOfWork,
    ILogger<ExternalLoginService> logger) : IExternalLoginService
{
    public Task<Result<ExternalSignInResponse>> SignInAsync(
        ExternalSignInRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<ExternalSignInResponse>>(logger, "ExternalSignIn", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            var result = await unitOfWork.ExecuteInTransactionAsync(
                ct => SignInCoreAsync(request.Register, ct),
                // Cada error de negocio deja su auditoría, y con una cuenta inactiva de correo confirmado también el
                // vínculo nuevo: el error se confirma. Una excepción igual deshace todo.
                CommitPolicy.OnAnyResult,
                cancellationToken);

            if (result.IsFailure)
            {
                return result.Error;
            }

            // La cookie de la aplicación sale recién después del commit de la cuenta, el vínculo y la auditoría, como en
            // los otros dos ingresos: SignInAsync lanza adentro de un límite.
            await signIn.SignInAsync(result.Value, cancellationToken);

            return new ExternalSignInResponse(request.ReturnUrl!);
        });
    }

    private async Task<Result<Guid>> SignInCoreAsync(bool? register, CancellationToken cancellationToken)
    {
        var login = await signIn.GetExternalLoginAsync(cancellationToken);
        if (login is null)
        {
            return Fail(identifier: string.Empty, user: null, ExternalLoginErrors.Failed);
        }

        // La cookie externa sirve solo para este callback.
        await signIn.SignOutExternalAsync(cancellationToken);

        var user = await users.FindByExternalLoginAsync(login.Provider, login.ProviderKey, cancellationToken);
        if (register == true && user is not null)
        {
            return Fail(AuditIdentifierOf(user, login), user, AccountErrors.AlreadyRegistered);
        }
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
            if (register == true && user is not null)
            {
                return Fail(email.Value.Value, user, AccountErrors.AlreadyRegistered);
            }
            if (user is null)
            {
                user = await users.FindByEmailAsync(email.Value, cancellationToken);
                if (register == true && user is not null)
                {
                    return Fail(email.Value.Value, user, AccountErrors.AlreadyRegistered);
                }
                if (user is null)
                {
                    // Primero la política y después la intención: con el registro cerrado no hay cuenta que crear ni
                    // «Crear cuenta» que ofrecer, así que entrar sin cuenta también dice que hace falta una invitación.
                    if (!await accountCreation.AllowsNewAccountAsync(email.Value, cancellationToken))
                    {
                        return Fail(email.Value.Value, user: null, AccountErrors.NotInvited);
                    }

                    if (register == false && !accountCreation.IsInitialAdmin(email.Value))
                    {
                        return Fail(email.Value.Value, user: null, AccountErrors.NotRegistered);
                    }

                    if (await users.ExistsDeletedByEmailAsync(email.Value, cancellationToken))
                    {
                        return Fail(email.Value.Value, user: null, AccountErrors.Disabled);
                    }

                    // El nombre de Google nadie lo tipea: se recorta y se limpia acá, en la entrada, en lugar de
                    // rechazar el ingreso.
                    var preferences = await accountCreation.GetPreferencesAsync(fromCurrentRequest: true, cancellationToken);
                    user = await userRepository.CreateAsync(
                        email.Value, phone: null, phoneConfirmed: false,
                        AccountRules.FitExternalDisplayName(login.DisplayName),
                        preferences.DefaultCulture, cancellationToken, preferences.DefaultTimeZoneId);
                }
                else if (!user.EmailConfirmed)
                {
                    // N4 (2026-11-15): Google se vincula por el correo sólo a una cuenta que ya lo confirmó. El de una
                    // cuenta que cargó un administrador, o que volvió al restaurarla, pudo pasar a otra persona, y el
                    // dueño anterior conservar un Google con esa dirección: se entra primero con el código al correo, y
                    // después Google se vincula. Una cuenta desactivada responde lo de siempre, sin revelar más.
                    return Fail(email.Value.Value, user,
                        user.IsActive ? ExternalLoginErrors.AccountEmailNotConfirmed : AccountErrors.Disabled);
                }

                await userRepository.AddExternalLoginAsync(user.Id, login, cancellationToken);
            }
        }

        var auditIdentifier = AuditIdentifierOf(user, login);
        if (!user.IsActive)
        {
            return Fail(auditIdentifier, user, AccountErrors.Disabled);
        }

        // El bloqueo por intentos fallidos no se mira acá (N3): es de los códigos y frena a quien los prueba, y si frenara
        // a Google cualquiera le cerraría la entrada a otro mandando códigos equivocados a su correo.

        // Como el código y el enlace: entrar bien pone en cero los intentos fallidos.
        await signIn.ResetFailedAttemptsAsync(user.Id, cancellationToken);

        audits.Succeeded(auditIdentifier, user.Id, LoginMethod.Google);

        return user.Id;
    }

    private static string AuditIdentifierOf(UserAccount user, ExternalLogin login) =>
        user.Email ?? (Email.Create(login.Email) is { IsSuccess: true } googleEmail
            ? googleEmail.Value.Value
            : string.Empty);

    private Error Fail(string identifier, UserAccount? user, Error error) =>
        audits.Failed(identifier, user?.Id, LoginMethod.Google, error);
}
