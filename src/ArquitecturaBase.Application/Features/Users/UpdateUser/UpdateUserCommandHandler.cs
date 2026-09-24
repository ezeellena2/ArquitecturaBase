using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Features.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Users.UpdateUser;

/// <summary>
/// La edición de un administrador: nombre, roles y, si vienen, un correo o un número (sección 12 del spec del ingreso con
/// WhatsApp). Lo que cambia queda sin verificar, con las mismas reglas que el alta: un celular de un país habilitado, y
/// nunca el correo o el número de otra cuenta, activa o borrada. Si cambia el número, el anterior se suelta como en el
/// perfil: su contacto de WhatsApp deja de apuntar a la cuenta y los enlaces que el bot ya mandó a ese chat dejan de
/// servir (<see cref="PhoneNumberChange"/>). Las sesiones no se cierran: cambiar un dato no es cortar el acceso. Para eso
/// está desvincular.
/// </summary>
internal sealed class UpdateUserCommandHandler(
    IIdentityService identityService,
    UserGuards guards,
    ILoginCodeRepository destinations,
    UserContactParser contacts,
    PhoneNumberChange phoneChange,
    WhatsAppContactLinker contactLinker)
    : ICommandHandler<UpdateUserCommand>
{
    public async Task<Result> Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var emailResult = UserContactParser.ReadEmail(command.Email);

        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        // El país se controla más abajo, y solo si el número es nuevo: para saberlo hace falta la cuenta.
        var phoneResult = contacts.ParsePhone(command.Phone);

        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var (email, phone) = (emailResult.Value, phoneResult.Value);

        // Los locks van antes de leer la cuenta, en el mismo orden que el alta y el perfil: los destinos, como el ingreso,
        // y con un número, los contactos y la cuenta, como el bot. Mientras se espera al bot, el bot puede escribir la
        // cuenta (verifica el número del chat): leída antes, el guardado chocaría con su ConcurrencyStamp.
        await LockAsync(command.UserId, email, phone, cancellationToken);

        var user = await identityService.FindByIdAsync(command.UserId, cancellationToken);

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        // Lo que ya tenía no cambia, y conserva si estaba verificado: el formulario puede mandar de vuelta el mismo correo.
        // El número que ya tenía tampoco pasa por la regla de los países: puede ser de uno que no está habilitado.
        var newEmail = email is not null && !string.Equals(email.Value, user.Email, StringComparison.OrdinalIgnoreCase)
            ? email
            : null;
        var newPhone = phone is not null && phone.Value != user.PhoneNumber ? phone : null;

        if (newPhone is not null && contacts.EnsureCountryAllowed(newPhone) is { IsFailure: true } country)
        {
            return country.Error;
        }

        IReadOnlyCollection<string> roles = [.. command.Roles!.Distinct(StringComparer.Ordinal)];
        var known = await identityService.ListRoleNamesAsync(cancellationToken);

        if (roles.Any(role => !known.Contains(role, StringComparer.Ordinal)))
        {
            return RoleErrors.NotFound;
        }

        // Las reglas de la sección 8 del spec, que escribió la Tarea 5: nadie se saca a sí mismo el rol Admin y
        // siempre queda al menos un administrador activo.
        var allowed = await guards.EnsureRolesCanChangeAsync(command.UserId, roles, cancellationToken);

        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var taken = await EnsureFreeAsync(user.Id, newEmail, newPhone, cancellationToken);

        if (taken.IsFailure)
        {
            return taken.Error;
        }

        await identityService.SetDisplayNameAsync(command.UserId, command.DisplayName, cancellationToken);
        await identityService.SetRolesAsync(command.UserId, roles, cancellationToken);

        return await ChangeContactAsync(user.Id, newEmail, newPhone, cancellationToken);
    }

    private async Task LockAsync(Guid userId, Email? email, PhoneNumber? phone, CancellationToken cancellationToken)
    {
        if (email is not null)
        {
            await destinations.LockDestinationAsync(LoginCodeDestination.ForEmail(email), cancellationToken);
        }

        if (phone is not null)
        {
            await destinations.LockDestinationAsync(LoginCodeDestination.ForPhone(phone), cancellationToken);
            await phoneChange.LockAsync(userId, phone, cancellationToken);
        }
    }

    /// <summary>
    /// Un correo o un número de otra cuenta no se cargan. Tampoco los de una cuenta borrada, que los conserva y el índice
    /// único los sigue reservando (sección 6.1 del spec del ingreso con WhatsApp).
    /// </summary>
    private async Task<Result> EnsureFreeAsync(Guid userId, Email? email, PhoneNumber? phone, CancellationToken cancellationToken)
    {
        if (email is not null
            && (await identityService.FindByEmailAsync(email, cancellationToken) is { } emailOwner
                ? emailOwner.Id != userId
                : await identityService.IsDeletedEmailAsync(email, cancellationToken)))
        {
            return UserErrors.AlreadyExists;
        }

        if (phone is not null
            && (await identityService.FindByPhoneAsync(phone, cancellationToken) is { } phoneOwner
                ? phoneOwner.Id != userId
                : await identityService.IsDeletedPhoneAsync(phone, cancellationToken)))
        {
            return UserErrors.PhoneAlreadyExists;
        }

        return Result.Success();
    }

    /// <summary>
    /// Guarda lo que cambió, sin verificar. Si otra cuenta se quedó con el correo o el número entre la búsqueda y el
    /// guardado, es el mismo 409: la unidad de trabajo no guarda nada, y la transacción que abrieron los locks se deshace.
    /// </summary>
    private async Task<Result> ChangeContactAsync(Guid userId, Email? email, PhoneNumber? phone, CancellationToken cancellationToken)
    {
        if (email is not null)
        {
            try
            {
                await identityService.SetEmailAsync(userId, email, confirmed: false, cancellationToken);
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
                await identityService.SetPhoneAsync(userId, phone, confirmed: false, cancellationToken);
            }
            catch (UniqueConstraintViolationException)
            {
                return UserErrors.PhoneAlreadyExists;
            }

            // Después del número, y no antes: si el guardado fallara, el chat del número que la cuenta conserva sigue
            // siendo suyo. El contacto del número nuevo no se vincula: el número no está verificado, y el bot lo vincula
            // cuando la persona le escriba desde ahí.
            await contactLinker.UnlinkUserAsync(userId, cancellationToken);
            await phoneChange.VoidPendingLinksAsync(userId, cancellationToken);
        }

        return Result.Success();
    }
}
