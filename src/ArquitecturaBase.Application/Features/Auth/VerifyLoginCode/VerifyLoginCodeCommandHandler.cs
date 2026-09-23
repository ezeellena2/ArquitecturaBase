using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;

internal sealed class VerifyLoginCodeCommandHandler(
    ILoginCodeRepository loginCodes,
    ILoginAuditRepository loginAudits,
    IIdentityService identityService,
    ILoginCodeHasher codeHasher,
    ISystemSettingsReader systemSettings,
    IRequestInfo requestInfo,
    TimeProvider timeProvider)
    : ICommandHandler<VerifyLoginCodeCommand, VerifyLoginCodeResponse>
{
    public async Task<Result<VerifyLoginCodeResponse>> Handle(VerifyLoginCodeCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);

        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;
        var destination = LoginCodeDestination.ForEmail(email);

        // Los límites de la sección 5.3 se aplican de a un request por email.
        await loginCodes.LockDestinationAsync(destination, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var user = await identityService.FindByEmailAsync(email, cancellationToken);

        if (user is not null && await identityService.IsLockedOutAsync(user.Id, cancellationToken))
        {
            return Fail(email, user, AccountErrors.LockedOut, nowUtc);
        }

        // Sin un código de ingreso para ese email, el error es el mismo que el de un código incorrecto. Uno pedido
        // desde el perfil para vincular el correo no sirve para entrar.
        var loginCode = await loginCodes.GetLatestAsync(destination, LoginCodePurpose.SignIn, cancellationToken);
        var verification = loginCode?.Verify(codeHasher.Hash(destination, LoginCodePurpose.SignIn, command.Code!), nowUtc)
            ?? Result.Failure(LoginCodeErrors.Invalid(attemptsLeft: null));

        if (verification.IsFailure)
        {
            if (user is not null)
            {
                await identityService.RegisterFailedAttemptAsync(user.Id, cancellationToken);
            }

            return Fail(email, user, verification.Error, nowUtc);
        }

        if (user is null)
        {
            var created = await CreateAccountAsync(email, cancellationToken);

            if (created.IsFailure)
            {
                return Fail(email, user: null, created.Error, nowUtc);
            }

            user = created.Value;
        }

        // Se informa recién ahora: el usuario ya probó que el email es suyo.
        if (!user.IsActive)
        {
            return Fail(email, user, AccountErrors.Disabled, nowUtc);
        }

        await identityService.ResetFailedAttemptsAsync(user.Id, cancellationToken);
        await identityService.SignInAsync(user.Id, cancellationToken);

        loginAudits.Add(LoginAudit.Success(
            email.Value, user.Id, LoginMethod.Code, requestInfo.IpAddress, requestInfo.UserAgent, nowUtc));

        return new VerifyLoginCodeResponse(command.ReturnUrl!);
    }

    /// <summary>
    /// La cuenta de quien acaba de probar con el código que el correo es suyo y todavía no tiene una. Como el código ya
    /// se verificó, los rechazos se pueden decir con todas las letras, igual que con Google y en el mismo orden: primero
    /// el modo de registro y después la cuenta borrada.
    /// </summary>
    private async Task<Result<UserAccount>> CreateAccountAsync(Email email, CancellationToken cancellationToken)
    {
        // Solo Open crea cuentas; cualquier otro modo cierra. InviteOnly se sostenía solo porque el pedido no le manda
        // el código a un correo sin cuenta, y con dos canales esa defensa no alcanza (hallazgo 1 de la etapa 1,
        // sección 10 del spec del ingreso con WhatsApp).
        if (await systemSettings.GetRegistrationModeAsync(cancellationToken) is not RegistrationMode.Open)
        {
            return AccountErrors.NotInvited;
        }

        // Una cuenta borrada no aparece en ninguna búsqueda, así que sin esto se intentaría crear otra con el mismo
        // correo y el índice único la rechazaría con un 500. Se informa como cuenta deshabilitada, que es lo que es.
        if (await identityService.IsDeletedEmailAsync(email, cancellationToken))
        {
            return AccountErrors.Disabled;
        }

        return await identityService.CreateAsync(
            email, phone: null, phoneConfirmed: false, displayName: null, UserCultures.FromCurrentRequest(), cancellationToken);
    }

    private Error Fail(Email email, UserAccount? user, Error error, DateTime nowUtc)
    {
        loginAudits.Add(LoginAudit.Failure(
            email.Value, user?.Id, LoginMethod.Code, error.Code, requestInfo.IpAddress, requestInfo.UserAgent, nowUtc));

        return error;
    }
}
