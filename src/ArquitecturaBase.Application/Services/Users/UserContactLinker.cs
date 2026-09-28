using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// El correo y el número que carga un administrador en el alta o en la edición (sección 12 del spec del ingreso con
/// WhatsApp), en pasos separados para que <see cref="UserAdministrationService"/> los intercale en el orden de cada caso
/// de uso: leerlos, tomar los locks, crear o restaurar la cuenta, comprobar que estén libres y cambiarlos. No abre ni
/// confirma transacciones: trabaja dentro del límite del servicio, y las escrituras de Identity autoguardan en esa misma
/// transacción, así que un error de negocio la deshace entera.
/// <para>
/// Vacío es lo mismo que no haberlo mandado. Un número nuevo pasa por las mismas reglas que en el ingreso: tiene que ser
/// un celular (<c>Users.Phone.Invalid</c>) de un país habilitado (<c>Auth.WhatsApp.CountryNotSupported</c>), porque es
/// con el que la persona va a entrar. La regla del país es solo para uno nuevo: una cuenta puede tener un número de otro
/// país (el bot crea cuentas con el número del chat, y achicar <c>WhatsApp:AllowedCountries</c> deja afuera números que
/// ya estaban), y la edición que lo manda de vuelta no lo cambia.
/// </para>
/// </summary>
internal sealed class UserContactLinker(
    IUserReader users,
    IUserRepository userRepository,
    ILoginCodeRepository destinations,
    PhoneNumberLinker phoneLinker,
    IPhoneNumberParser phoneNumbers,
    IOptions<WhatsAppLoginOptions> whatsAppOptions)
{
    public static Result<Email?> ReadEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Result.Success<Email?>(null);
        }

        var parsed = Email.Create(email);

        return parsed.IsSuccess ? Result.Success<Email?>(parsed.Value) : Result.Failure<Email?>(parsed.Error);
    }

    /// <summary>El número de un alta, que siempre es nuevo: tiene que ser un celular de un país habilitado.</summary>
    public Result<PhoneNumber?> ReadPhone(PhoneNumberInput? phone)
    {
        var parsed = ParsePhone(phone);

        if (parsed.IsFailure || parsed.Value is null)
        {
            return parsed;
        }

        var allowed = EnsureCountryAllowed(parsed.Value);

        return allowed.IsSuccess ? parsed : Result.Failure<PhoneNumber?>(allowed.Error);
    }

    /// <summary>
    /// Solo la forma: un celular, de cualquier país. La edición lo usa antes de saber si el número es nuevo, y controla el
    /// país (<see cref="EnsureCountryAllowed"/>) recién si lo es.
    /// </summary>
    public Result<PhoneNumber?> ParsePhone(PhoneNumberInput? phone)
    {
        if (phone is null || phone.IsEmpty)
        {
            return Result.Success<PhoneNumber?>(null);
        }

        var parsed = phoneNumbers.Parse(phone.Country, phone.Number);

        return parsed.IsSuccess ? Result.Success<PhoneNumber?>(parsed.Value) : Result.Failure<PhoneNumber?>(parsed.Error);
    }

    /// <summary>Si se mandan códigos a números del país de <paramref name="phone"/>, como en el ingreso.</summary>
    public Result EnsureCountryAllowed(PhoneNumber phone) =>
        whatsAppOptions.Value.AllowsCountry(phoneNumbers.RegionOf(phone))
            ? Result.Success()
            : WhatsAppErrors.CountryNotSupported;

    /// <summary>
    /// Los locks del alta: correo primero y teléfono después, el mismo orden que la edición, así dos pedidos con los
    /// mismos destinos se ponen en fila en lugar de trabarse en un deadlock. Son los mismos locks que toma el ingreso con
    /// código.
    /// </summary>
    public async Task LockDestinationsAsync(Email? email, PhoneNumber? phone, CancellationToken cancellationToken)
    {
        if (email is not null)
        {
            await destinations.LockDestinationAsync(LoginCodeDestination.ForEmail(email), cancellationToken);
        }

        if (phone is not null)
        {
            await destinations.LockDestinationAsync(LoginCodeDestination.ForPhone(phone), cancellationToken);
        }
    }

    /// <summary>
    /// Los locks de la edición: los destinos (<see cref="LockDestinationsAsync"/>) y, con número, los contactos y después
    /// los enlaces de la cuenta (<see cref="PhoneNumberLinker.LockAsync"/>): el mismo orden del bot y del perfil. Quien
    /// llama lee la cuenta después.
    /// </summary>
    public async Task LockAsync(Guid userId, Email? email, PhoneNumber? phone, CancellationToken cancellationToken)
    {
        await LockDestinationsAsync(email, phone, cancellationToken);
        if (phone is not null)
        {
            await phoneLinker.LockAsync(userId, phone, cancellationToken);
        }
    }

    /// <summary>
    /// Crea la cuenta sin verificar o restaura la borrada a la que pertenecen todos los datos aportados. Quien llama ya
    /// tomó los locks de los destinos (<see cref="LockDestinationsAsync"/>).
    /// </summary>
    public async Task<Result<UserAccount>> CreateOrRestoreAsync(
        Email? email, PhoneNumber? phone, string? displayName, CancellationToken cancellationToken)
    {
        if (email is not null && await users.FindByEmailAsync(email, cancellationToken) is not null)
        {
            return UserErrors.AlreadyExists;
        }

        if (phone is not null && await users.FindByPhoneAsync(phone, cancellationToken) is not null)
        {
            return UserErrors.PhoneAlreadyExists;
        }

        var deletedByEmail = email is null ? null : await users.FindDeletedByEmailAsync(email, cancellationToken);
        var deletedByPhone = phone is null ? null : await users.FindDeletedByPhoneAsync(phone, cancellationToken);
        if (deletedByEmail is null && deletedByPhone is null)
        {
            try
            {
                return await userRepository.CreateUnverifiedAsync(
                    email, phone, displayName, UserCultures.FromCurrentRequest(), cancellationToken);
            }
            catch (UniqueConstraintViolationException)
            {
                return await TakenErrorAsync(email, phone, cancellationToken);
            }
        }

        // Solo se restaura la cuenta a la que pertenecen todos los datos aportados.
        if (deletedByEmail is not null)
        {
            if (deletedByPhone is not null && deletedByPhone.Id != deletedByEmail.Id)
            {
                return UserErrors.PhoneAlreadyExists;
            }

            if (phone is not null && deletedByEmail.PhoneNumber is { } otherPhone && otherPhone != phone.Value)
            {
                return UserErrors.AlreadyExists;
            }

            return await RestoreAsync(deletedByEmail, email, phone, displayName, cancellationToken);
        }

        if (email is not null
            && deletedByPhone!.Email is { } otherEmail
            && !string.Equals(otherEmail, email.Value, StringComparison.OrdinalIgnoreCase))
        {
            return UserErrors.PhoneAlreadyExists;
        }

        return await RestoreAsync(deletedByPhone!, email, phone, displayName, cancellationToken);
    }

    /// <summary>
    /// Que el correo y el número nuevos de <paramref name="userId"/> no sean de otra cuenta, tampoco de una borrada. Null
    /// es que no cambia.
    /// </summary>
    public async Task<Result> EnsureFreeAsync(
        Guid userId, Email? email, PhoneNumber? phone, CancellationToken cancellationToken)
    {
        if (email is not null
            && (await users.FindByEmailAsync(email, cancellationToken) is { } emailOwner
                ? emailOwner.Id != userId
                : await users.ExistsDeletedByEmailAsync(email, cancellationToken)))
        {
            return UserErrors.AlreadyExists;
        }

        if (phone is not null
            && (await users.FindByPhoneAsync(phone, cancellationToken) is { } phoneOwner
                ? phoneOwner.Id != userId
                : await users.ExistsDeletedByPhoneAsync(phone, cancellationToken)))
        {
            return UserErrors.PhoneAlreadyExists;
        }

        return Result.Success();
    }

    /// <summary>
    /// Guarda sin verificar el correo y el número nuevos (null es que no cambia). Con número nuevo, suelta el contacto de
    /// la cuenta e invalida los enlaces que el bot pudo haber mandado al chat del anterior.
    /// </summary>
    public async Task<Result> ChangeAsync(
        Guid userId, Email? email, PhoneNumber? phone, CancellationToken cancellationToken)
    {
        if (email is not null)
        {
            try
            {
                await userRepository.SetEmailAsync(userId, email, confirmed: false, cancellationToken);
            }
            catch (UniqueConstraintViolationException)
            {
                return UserErrors.AlreadyExists;
            }
        }

        if (phone is not null)
        {
            try
            {
                await userRepository.SetPhoneAsync(userId, phone, confirmed: false, cancellationToken);
            }
            catch (UniqueConstraintViolationException)
            {
                return UserErrors.PhoneAlreadyExists;
            }

            await phoneLinker.ReleaseContactAsync(userId, cancellationToken);
            await phoneLinker.VoidPendingLinksAsync(userId, cancellationToken);
        }

        return Result.Success();
    }

    private async Task<Error> TakenErrorAsync(Email? email, PhoneNumber? phone, CancellationToken cancellationToken)
    {
        if (phone is null)
        {
            return UserErrors.AlreadyExists;
        }

        if (email is null)
        {
            return UserErrors.PhoneAlreadyExists;
        }

        var emailTaken = await users.FindByEmailAsync(email, cancellationToken) is not null
            || await users.ExistsDeletedByEmailAsync(email, cancellationToken);
        return emailTaken ? UserErrors.AlreadyExists : UserErrors.PhoneAlreadyExists;
    }

    private async Task<Result<UserAccount>> RestoreAsync(
        UserAccount deleted, Email? email, PhoneNumber? phone, string? displayName, CancellationToken cancellationToken)
    {
        await userRepository.RestoreAsync(deleted.Id, displayName, cancellationToken);

        try
        {
            if (email is not null)
            {
                await userRepository.SetEmailAsync(deleted.Id, email, confirmed: false, cancellationToken);
            }
        }
        catch (UniqueConstraintViolationException)
        {
            return UserErrors.AlreadyExists;
        }

        try
        {
            if (phone is not null)
            {
                await userRepository.SetPhoneAsync(deleted.Id, phone, confirmed: false, cancellationToken);
            }
        }
        catch (UniqueConstraintViolationException)
        {
            return UserErrors.PhoneAlreadyExists;
        }

        return await users.FindByIdAsync(deleted.Id, cancellationToken)
            ?? throw new InvalidOperationException("The restored account was not found.");
    }
}
