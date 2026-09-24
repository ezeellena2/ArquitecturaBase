using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Persistence;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Users.CreateUser;

/// <summary>
/// El alta de un administrador. Lo que carga queda sin verificar hasta que la persona entra con eso (sección 6.1 del spec
/// del ingreso con WhatsApp). El correo y el número son únicos, también contra las cuentas borradas, que los conservan:
/// si todo lo cargado es de una misma cuenta borrada, esa se restaura, con los roles del alta y no con los que tenía
/// (sección 7 del spec de la Fase 4); si el correo o el número es de una cuenta borrada que tiene otro, o son de cuentas
/// distintas, es un 409. Crear y mandar la invitación es una sola operación: si la cuenta no se crea, no sale nada.
/// </summary>
internal sealed class CreateUserCommandHandler(
    IIdentityService identityService,
    ILoginCodeRepository destinations,
    UserContactParser contacts,
    UserInvitationSender invitationSender)
    : ICommandHandler<CreateUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var emailResult = UserContactParser.ReadEmail(command.Email);

        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var phoneResult = contacts.ReadPhone(command.Phone);

        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var (email, phone) = (emailResult.Value, phoneResult.Value);

        if (email is null && phone is null)
        {
            return UserErrors.IdentityRequired;
        }

        if (command.Invitation is { Channel: { } channel } invitation)
        {
            var allowed = invitationSender.Check(
                channel, invitation.Consent, command.DisplayName, email is not null, phone is not null, InvitationFields.OfCreate);

            if (allowed.IsFailure)
            {
                return allowed.Error;
            }
        }

        // Sin roles en el alta, la cuenta queda igual que una que se creó sola al ingresar.
        IReadOnlyCollection<string> roles = command.Roles is { Count: > 0 }
            ? [.. command.Roles.Distinct(StringComparer.Ordinal)]
            : [SystemRoles.User];

        var known = await identityService.ListRoleNamesAsync(cancellationToken);

        if (roles.Any(role => !known.Contains(role, StringComparer.Ordinal)))
        {
            return RoleErrors.NotFound;
        }

        // El lock de cada destino, el mismo que toma el ingreso con código antes de crear una cuenta: así el alta y un
        // ingreso que crea la cuenta del mismo correo o número pasan de a uno, y el segundo ve lo que guardó el primero.
        // El bot y Google no lo toman: si uno de ellos gana, el alta choca con el índice único y es el mismo 409 (ver
        // CreateOrRestoreAsync). Abre la transacción, así la cuenta, sus roles y la invitación se guardan juntos.
        await LockDestinationsAsync(email, phone, cancellationToken);

        var account = await CreateOrRestoreAsync(email, phone, command.DisplayName, cancellationToken);

        if (account.IsFailure)
        {
            return account.Error;
        }

        await identityService.SetRolesAsync(account.Value.Id, roles, cancellationToken);

        if (command.Invitation?.Channel is { } requested)
        {
            await invitationSender.SendAsync(account.Value, requested, cancellationToken);
        }

        return account.Value.Id;
    }

    // Siempre en el mismo orden, primero el correo: dos altas con el mismo correo y el mismo número no se esperan en cruz.
    private async Task LockDestinationsAsync(Email? email, PhoneNumber? phone, CancellationToken cancellationToken)
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

    private async Task<Result<UserAccount>> CreateOrRestoreAsync(
        Email? email,
        PhoneNumber? phone,
        string? displayName,
        CancellationToken cancellationToken)
    {
        if (email is not null && await identityService.FindByEmailAsync(email, cancellationToken) is not null)
        {
            return UserErrors.AlreadyExists;
        }

        if (phone is not null && await identityService.FindByPhoneAsync(phone, cancellationToken) is not null)
        {
            return UserErrors.PhoneAlreadyExists;
        }

        var deletedByEmail = email is null ? null : await identityService.FindDeletedByEmailAsync(email, cancellationToken);
        var deletedByPhone = phone is null ? null : await identityService.FindDeletedByPhoneAsync(phone, cancellationToken);

        if (deletedByEmail is null && deletedByPhone is null)
        {
            try
            {
                return await identityService.CreateUnverifiedAsync(
                    email, phone, displayName, UserCultures.FromCurrentRequest(), cancellationToken);
            }
            catch (UniqueConstraintViolationException)
            {
                // Otra cuenta se quedó con el correo o el número entre la búsqueda y el guardado: el bot («Crear
                // cuenta») o Google, que crean cuentas sin el lock del destino. Es el mismo 409 que si la búsqueda la
                // hubiera visto.
                return await TakenErrorAsync(email, phone, cancellationToken);
            }
        }

        // Se restaura solo si todo lo cargado es de esa cuenta: puede completarle lo que no tenía, pero nunca reemplazarle
        // el correo o el número. Si no, es otra persona, y restaurarla le daría la historia y los medios de ingreso de la
        // cuenta borrada (un Google vinculado, por ejemplo). El correo decide qué cuenta es; el que choca es el otro dato.
        if (deletedByEmail is not null)
        {
            // El número es de otra cuenta borrada.
            if (deletedByPhone is not null && deletedByPhone.Id != deletedByEmail.Id)
            {
                return UserErrors.PhoneAlreadyExists;
            }

            // La cuenta del correo tiene otro número.
            if (phone is not null && deletedByEmail.PhoneNumber is { } otherPhone && otherPhone != phone.Value)
            {
                return UserErrors.AlreadyExists;
            }

            return await RestoreAsync(deletedByEmail, email, phone, displayName, cancellationToken);
        }

        // La cuenta del número tiene otro correo.
        if (email is not null
            && deletedByPhone!.Email is { } otherEmail
            && !string.Equals(otherEmail, email.Value, StringComparison.OrdinalIgnoreCase))
        {
            return UserErrors.PhoneAlreadyExists;
        }

        return await RestoreAsync(deletedByPhone!, email, phone, displayName, cancellationToken);
    }

    /// <summary>
    /// Cuál de los dos chocó con el índice único. Si vino uno solo, es ese. Si vinieron los dos, se vuelve a buscar el
    /// correo, que ahora se ve: la base rechaza el guardado recién cuando el otro pedido confirmó. Si no es el correo, es
    /// el número. El correo se mira primero, como en la búsqueda de antes.
    /// </summary>
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

        var emailTaken = await identityService.FindByEmailAsync(email, cancellationToken) is not null
            || await identityService.IsDeletedEmailAsync(email, cancellationToken);

        return emailTaken ? UserErrors.AlreadyExists : UserErrors.PhoneAlreadyExists;
    }

    /// <summary>
    /// Restaura la cuenta borrada, con el nombre del alta, y le pone lo que cargó el alta sin verificar: el correo y el
    /// número que ya tenía, o el que le faltaba. Lo que el alta no cargó queda como estaba.
    /// </summary>
    private async Task<Result<UserAccount>> RestoreAsync(
        UserAccount deleted,
        Email? email,
        PhoneNumber? phone,
        string? displayName,
        CancellationToken cancellationToken)
    {
        await identityService.RestoreAsync(deleted.Id, displayName, cancellationToken);

        try
        {
            if (email is not null)
            {
                await identityService.SetEmailAsync(deleted.Id, email, confirmed: false, cancellationToken);
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
                await identityService.SetPhoneAsync(deleted.Id, phone, confirmed: false, cancellationToken);
            }
        }
        catch (UniqueConstraintViolationException)
        {
            return UserErrors.PhoneAlreadyExists;
        }

        return await identityService.FindByIdAsync(deleted.Id, cancellationToken)
            ?? throw new InvalidOperationException("The restored account was not found.");
    }
}
