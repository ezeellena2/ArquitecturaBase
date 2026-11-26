using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Le pone a la cuenta el correo que confirmó desde el perfil: revisa el código con
/// <see cref="DestinationCodeVerifier"/>, lo gasta y guarda el correo, ya verificado, si está libre. Lo que no se puede
/// hacer se rechaza antes de revisar ningún código, así no se gasta nada:
/// <list type="bullet">
/// <item>la cuenta de <c>Seed:AdminEmail</c> no cambia de correo (N25): el arranque la busca por ese correo para
/// devolverle Admin, y con otro la perdería;</item>
/// <item>un correo verificado no se cambia desde el perfil (N11), como tampoco lo ofrece la pantalla; uno sin verificar
/// (lo cargó un administrador) sí se reemplaza;</item>
/// <item>con un número verificado, hace falta además el código que confirma el cambio, mandado a ese número (cada
/// medio confirma los cambios del otro, 2026-11-16: con una sesión abierta ajena, agregar un correo propio y
/// desvincular el número dejaría afuera a su dueño). Es de <see cref="LoginCodePurpose.ConfirmChange"/> y lo pide el
/// canal telefónico (con WhatsApp, <c>POST /api/me/whatsapp/confirmation-code</c>); apagado, no se puede. Sin número
/// verificado no hay a quién pedírselo, y alcanza con el código del correo nuevo.</item>
/// </list>
/// Los dos códigos se revisan antes de gastar alguno, en el orden único de los locks (el correo antes que el número):
/// uno equivocado cuenta su intento y el otro sigue sirviendo. El error del código del número va atado al campo
/// <see cref="PhoneCodeField"/>. No guarda por su cuenta: corre adentro
/// del límite de <see cref="ProfileService"/>, con OnAnyResult.
/// </summary>
internal sealed class ProfileEmailLinker(
    IUserReader users,
    IUserRepository userRepository,
    DestinationCodeVerifier verifier,
    IInitialAdmin initialAdmin,
    IPhoneChannel phoneChannel)
{
    /// <summary>El campo del pedido con el código que llegó al número de la cuenta.</summary>
    public const string PhoneCodeField = "phoneCode";

    public async Task<Result> LinkAsync(
        UserAccount user, Email email, string code, string? phoneCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(email);

        var replaces = user.Email is not null && !string.Equals(user.Email, email.Value, StringComparison.OrdinalIgnoreCase);
        if (replaces && Email.Create(user.Email) is { IsSuccess: true } current && initialAdmin.IsInitialAdmin(current.Value))
        {
            return UserErrors.InitialAdmin;
        }

        if (replaces && user.EmailConfirmed)
        {
            return UserErrors.EmailAlreadySet;
        }

        // Lo que falta se rechaza antes de revisar ningún código.
        var phone = VerifiedPhoneOf(user);
        if (phone is not null)
        {
            if (!phoneChannel.IsEnabled)
            {
                return UserErrors.PhoneCodeUnavailable;
            }

            if (string.IsNullOrEmpty(phoneCode))
            {
                return FieldErrors.On(UserErrors.ConfirmationCodeRequired, PhoneCodeField);
            }
        }

        // El orden único de los locks: el correo antes que el número (como la edición de la administración).
        var emailCheck = await verifier.CheckAsync(
            LoginCodeDestination.ForEmail(email), LoginCodePurpose.VerifyDestination, user.Id, code, cancellationToken);
        if (emailCheck.IsFailure)
        {
            return emailCheck.Error;
        }

        CheckedCode? phoneCheck = null;
        if (phone is not null)
        {
            var check = await verifier.CheckAsync(
                LoginCodeDestination.ForPhone(phone), LoginCodePurpose.ConfirmChange, user.Id, phoneCode!, cancellationToken);
            if (check.IsFailure)
            {
                return FieldErrors.OnWithMetadata(check.Error, PhoneCodeField);
            }

            phoneCheck = check.Value;
        }

        // Los dos estaban bien: se gastan juntos, aunque después el correo resulte de otra cuenta.
        DestinationCodeVerifier.Consume(emailCheck.Value);
        if (phoneCheck is not null)
        {
            DestinationCodeVerifier.Consume(phoneCheck);
        }

        // La fila de la cuenta, después de los locks de los códigos y antes de escribir: una edición de la administración en
        // curso termina antes, y el guardado no choca con su ConcurrencyStamp. El cambio avanza la revisión (N2), pero no
        // la pide: lo confirma el código.
        await userRepository.LockAsync(user.Id, cancellationToken);

        // El pedido del código ya lo miró (O28), pero otra cuenta pudo tomarlo después.
        if (await IsTakenAsync(user.Id, email, cancellationToken))
        {
            return UserErrors.AlreadyExists;
        }

        try
        {
            await userRepository.SetEmailAsync(user.Id, email, confirmed: true, cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // El savepoint deshizo solo ese guardado: el código sigue gastado y se confirma igual.
            return UserErrors.AlreadyExists;
        }

        return Result.Success();
    }

    /// <summary>
    /// El correo es de otra cuenta, activa o borrada: el índice único conserva también los correos de cuentas borradas. El
    /// propio sin verificar (lo cargó un administrador) no cuenta.
    /// </summary>
    public async Task<bool> IsTakenAsync(Guid userId, Email email, CancellationToken cancellationToken)
    {
        var owner = await users.FindByEmailAsync(email, cancellationToken);

        return owner is null ? await users.ExistsDeletedByEmailAsync(email, cancellationToken) : owner.Id != userId;
    }

    private static PhoneNumber? VerifiedPhoneOf(UserAccount user) =>
        user is { PhoneNumber: { } value, PhoneNumberConfirmed: true } && PhoneNumber.Create(value) is { IsSuccess: true } phone
            ? phone.Value
            : null;
}
