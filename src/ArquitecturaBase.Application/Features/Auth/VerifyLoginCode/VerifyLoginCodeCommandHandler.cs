using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;

/// <summary>
/// Verifica el código que llegó por correo o por WhatsApp e inicia la sesión. Los dos canales recorren el mismo camino
/// (sección 10 del spec del ingreso con WhatsApp); lo que cambia entre uno y otro lo sabe <see cref="SignInIdentifier"/>.
/// </summary>
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
        var identifierResult = SignInIdentifier.From(command, identityService);

        if (identifierResult.IsFailure)
        {
            return identifierResult.Error;
        }

        var identifier = identifierResult.Value;

        // Los límites de la sección 5.3 se aplican de a un request por destino.
        await loginCodes.LockDestinationAsync(identifier.Destination, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var user = await identifier.FindAccountAsync(cancellationToken);

        if (user is not null && await identityService.IsLockedOutAsync(user.Id, cancellationToken))
        {
            return Fail(identifier, user, AccountErrors.LockedOut, nowUtc);
        }

        // Sin un código de ingreso para ese destino, el error es el mismo que el de un código incorrecto. Uno pedido
        // desde el perfil para vincular el correo o el número no sirve para entrar.
        var loginCode = await loginCodes.GetLatestAsync(identifier.Destination, LoginCodePurpose.SignIn, cancellationToken);
        var verification = loginCode?.Verify(
                codeHasher.Hash(identifier.Destination, LoginCodePurpose.SignIn, command.Code!), nowUtc)
            ?? Result.Failure(LoginCodeErrors.Invalid(attemptsLeft: null));

        if (verification.IsFailure)
        {
            if (user is not null)
            {
                await identityService.RegisterFailedAttemptAsync(user.Id, cancellationToken);
            }

            return Fail(identifier, user, verification.Error, nowUtc);
        }

        if (user is null)
        {
            var created = await CreateAccountAsync(identifier, cancellationToken);

            if (created.IsFailure)
            {
                return Fail(identifier, user: null, created.Error, nowUtc);
            }

            user = created.Value;
        }
        else
        {
            await identifier.ConfirmAsync(user, cancellationToken);
        }

        // Se informa recién ahora: el usuario ya probó que el correo o el número es suyo.
        if (!user.IsActive)
        {
            return Fail(identifier, user, AccountErrors.Disabled, nowUtc);
        }

        await identityService.ResetFailedAttemptsAsync(user.Id, cancellationToken);
        await identityService.SignInAsync(user.Id, cancellationToken);

        loginAudits.Add(LoginAudit.Success(
            identifier.Destination.Value, user.Id, identifier.Method, requestInfo.IpAddress, requestInfo.UserAgent, nowUtc));

        return new VerifyLoginCodeResponse(command.ReturnUrl!);
    }

    /// <summary>
    /// La cuenta de quien acaba de probar con el código que el correo o el número es suyo y todavía no tiene una. Como
    /// el código ya se verificó, los rechazos se pueden decir con todas las letras, igual que con Google y en el mismo
    /// orden: primero el modo de registro y después la cuenta borrada.
    /// </summary>
    private async Task<Result<UserAccount>> CreateAccountAsync(SignInIdentifier identifier, CancellationToken cancellationToken)
    {
        // Solo Open crea cuentas; cualquier otro modo cierra. InviteOnly se sostenía solo porque el pedido no le manda
        // el código a un destino sin cuenta, y con dos canales esa defensa no alcanza (hallazgo 1 de la etapa 1,
        // sección 10 del spec del ingreso con WhatsApp).
        if (await systemSettings.GetRegistrationModeAsync(cancellationToken) is not RegistrationMode.Open)
        {
            return AccountErrors.NotInvited;
        }

        // Una cuenta borrada no aparece en ninguna búsqueda, así que sin esto se intentaría crear otra con el mismo
        // correo o número y el índice único la rechazaría con un 500. Se informa como cuenta deshabilitada, que es lo
        // que es.
        if (await identifier.BelongsToDeletedAccountAsync(cancellationToken))
        {
            return AccountErrors.Disabled;
        }

        return await identifier.CreateAccountAsync(UserCultures.FromCurrentRequest(), cancellationToken);
    }

    private Error Fail(SignInIdentifier identifier, UserAccount? user, Error error, DateTime nowUtc)
    {
        loginAudits.Add(LoginAudit.Failure(
            identifier.Destination.Value, user?.Id, identifier.Method, error.Code, requestInfo.IpAddress, requestInfo.UserAgent, nowUtc));

        return error;
    }

    /// <summary>
    /// Con qué se presenta la persona: el correo o el número. Cada uno sabe su destino (el del lock, el código y la
    /// auditoría), su método de ingreso, cómo se busca su cuenta, cómo se reconoce una cuenta borrada y cómo se crea
    /// una. El resto del ingreso es el mismo para los dos.
    /// </summary>
    private abstract class SignInIdentifier(LoginCodeDestination destination, LoginMethod method)
    {
        /// <summary>El correo normalizado o el número en formato internacional: lo que queda en la auditoría.</summary>
        public LoginCodeDestination Destination { get; } = destination;

        public LoginMethod Method { get; } = method;

        /// <summary>Con el número si vino (el validador ya controló que venga uno solo); si no, con el correo.</summary>
        public static Result<SignInIdentifier> From(VerifyLoginCodeCommand command, IIdentityService identity)
        {
            if (command.IsByPhone)
            {
                var phone = PhoneNumber.Create(command.Phone);

                return phone.IsSuccess ? new PhoneIdentifier(phone.Value, identity) : phone.Error;
            }

            var email = Email.Create(command.Email);

            return email.IsSuccess ? new EmailIdentifier(email.Value, identity) : email.Error;
        }

        public abstract Task<UserAccount?> FindAccountAsync(CancellationToken cancellationToken);

        public abstract Task<bool> BelongsToDeletedAccountAsync(CancellationToken cancellationToken);

        public abstract Task<UserAccount> CreateAccountAsync(string culture, CancellationToken cancellationToken);

        /// <summary>Lo que cambia en una cuenta que ya existía, ahora que la persona probó que el destino es suyo.</summary>
        public virtual Task ConfirmAsync(UserAccount user, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>El correo, como hasta ahora: el alta lo deja verificado y una cuenta existente no cambia.</summary>
    private sealed class EmailIdentifier(Email email, IIdentityService identity)
        : SignInIdentifier(LoginCodeDestination.ForEmail(email), LoginMethod.Code)
    {
        public override Task<UserAccount?> FindAccountAsync(CancellationToken cancellationToken) =>
            identity.FindByEmailAsync(email, cancellationToken);

        public override Task<bool> BelongsToDeletedAccountAsync(CancellationToken cancellationToken) =>
            identity.IsDeletedEmailAsync(email, cancellationToken);

        public override Task<UserAccount> CreateAccountAsync(string culture, CancellationToken cancellationToken) =>
            identity.CreateAsync(email, phone: null, phoneConfirmed: false, displayName: null, culture, cancellationToken);
    }

    /// <summary>
    /// El número de WhatsApp: el alta crea una cuenta sin correo, con el número verificado, y el número que cargó un
    /// administrador queda verificado cuando la persona entra con él (sección 6.1 del spec del ingreso con WhatsApp).
    /// </summary>
    private sealed class PhoneIdentifier(PhoneNumber phone, IIdentityService identity)
        : SignInIdentifier(LoginCodeDestination.ForPhone(phone), LoginMethod.WhatsAppCode)
    {
        public override Task<UserAccount?> FindAccountAsync(CancellationToken cancellationToken) =>
            identity.FindByPhoneAsync(phone, cancellationToken);

        public override Task<bool> BelongsToDeletedAccountAsync(CancellationToken cancellationToken) =>
            identity.IsDeletedPhoneAsync(phone, cancellationToken);

        public override Task<UserAccount> CreateAccountAsync(string culture, CancellationToken cancellationToken) =>
            identity.CreateAsync(email: null, phone, phoneConfirmed: true, displayName: null, culture, cancellationToken);

        public override Task ConfirmAsync(UserAccount user, CancellationToken cancellationToken) =>
            user.PhoneNumberConfirmed
                ? Task.CompletedTask
                : identity.SetPhoneAsync(user.Id, phone, confirmed: true, cancellationToken);
    }
}
