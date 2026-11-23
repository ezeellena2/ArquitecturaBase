using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// El alta, la edición y el reenvío de la invitación de una cuenta desde la administración. Cada método valida afuera y
/// abre un solo límite con <see cref="CommitPolicy.OnSuccess"/>: los locks preceden a las escrituras de Identity, que
/// autoguardan en esa misma transacción, y un error de negocio la deshace entera. El correo y el número los manejan los
/// pasos de <see cref="UserContactLinker"/>, y la invitación, los de <see cref="UserInvitationIssuer"/>, intercalados en
/// el orden de cada caso de uso. Los roles, la restauración, el correo y el número dejan su historia al final del trabajo,
/// en el mismo límite (<see cref="AdministrativeChangeRecorder"/>, N13).
/// </summary>
internal sealed class UserAdministrationService(
    IUserRepository userRepository,
    UserContactLinker contacts,
    UserInvitationIssuer invitations,
    UserGuard guard,
    AdministrativeChangeRecorder changes,
    IRequestValidator validator,
    IUnitOfWork unitOfWork,
    ILogger<UserAdministrationService> logger) : IUserAdministrationService
{
    public Task<Result<CreateUserResponse>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<CreateUserResponse>>(logger, "CreateUser", async () =>
        {
            // Afuera del límite: un pedido inválido no abre transacción ni toma locks.
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            // Identity autoguarda la cuenta, sus roles y la restauración adentro: un error de negocio deshace todo. La
            // invitación se encola antes del commit, así la fila queda guardada ya con su estado.
            return await unitOfWork.ExecuteInTransactionAsync(
                ct => CreateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }

    public Task<Result> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result>(logger, "UpdateUser", async () =>
        {
            // Afuera del límite: un pedido inválido no abre transacción ni toma locks.
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            // El nombre, los roles, el correo y el número se autoguardan por separado: adentro del límite quedan todos o
            // ninguno, también cuando no se toca el correo ni el número y no se toma ningún lock.
            return await unitOfWork.ExecuteInTransactionAsync(
                ct => UpdateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }

    public Task<Result> SendInvitationAsync(SendUserInvitationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result>(logger, "SendInvitation", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            return await unitOfWork.ExecuteInTransactionAsync(
                ct => SendInvitationCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }

    // El alta, ya validada: correo, teléfono, que haya al menos uno, las reglas de la invitación y los permisos de los
    // roles, todo antes de los locks; después, el alta o la restauración, que los roles existan, los roles y el envío.
    private async Task<Result<CreateUserResponse>> CreateCoreAsync(
        CreateUserRequest request, CancellationToken cancellationToken)
    {
        var emailResult = UserContactLinker.ReadEmail(request.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var phoneResult = await contacts.ReadPhoneAsync(request.Phone, cancellationToken);
        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var (email, phone) = (emailResult.Value, phoneResult.Value);
        if (email is null && phone is null)
        {
            return UserErrors.IdentityRequired;
        }

        if (request.Invitation is { Channel: { } channel } invitation)
        {
            var allowed = invitations.Check(
                channel, invitation.Consent, request.DisplayName, email is not null, phone is not null,
                InvitationFields.OfCreate);
            if (allowed.IsFailure)
            {
                return allowed.Error;
            }
        }

        IReadOnlyCollection<string> requestedRoles = request.Roles is { Count: > 0 }
            ? [.. request.Roles.Distinct(StringComparer.Ordinal)]
            : [SystemRoles.User];
        // Con los permisos de quien pide, leídos de la base adentro del límite: nadie da lo que no tiene. Un rol que no
        // existe no tiene permisos que dar; lo rechaza EnsureRolesExistAsync, después de los locks.
        var rolesGiven = await guard.EnsureRolesCanBeGivenAsync(requestedRoles, email, cancellationToken);
        if (rolesGiven.IsFailure)
        {
            return rolesGiven.Error;
        }

        await contacts.LockDestinationsAsync(email, phone, cancellationToken);
        var created = await contacts.CreateOrRestoreAsync(email, phone, request.DisplayName, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error;
        }

        // Los roles pedidos (FOR KEY SHARE) recién ahora, después de los destinos y de la fila de la cuenta que se
        // restaura, en el orden de backend.md: tomados antes, el alta podía esperar un destino teniendo el rol mientras una
        // edición con ese destino esperaba el rol detrás de su borrado. Un rol borrado en el medio da NotFound (N2).
        var rolesExist = await guard.EnsureRolesExistAsync(requestedRoles, cancellationToken);
        if (rolesExist.IsFailure)
        {
            return rolesExist.Error;
        }

        var account = created.Value.Account;
        var assignment = await userRepository.SetRolesAsync(account.Id, requestedRoles, cancellationToken);

        // Otra vez, con lo que quedó: los roles asignados, por id, y el correo de la cuenta. Una restaurada tiene sólo el
        // correo del alta (N4): si era la de Seed:AdminEmail y se restauró por el número, ya no lo tiene. Un rechazo
        // deshace también la restauración.
        var assigned = await guard.EnsureAssignedRolesAsync(account, [], cancellationToken);
        if (assigned.IsFailure)
        {
            return assigned.Error;
        }

        if (request.Invitation?.Channel is { } requestedChannel)
        {
            // Toma el lock de invitaciones de la cuenta antes de agregar y encolar: la cola de WhatsApp lo pide para
            // encontrar la invitación después del commit. En un tope diario del correo (N7) se deshace también el alta.
            var sent = await invitations.SendAsync(account, requestedChannel, cancellationToken);
            if (sent.IsFailure)
            {
                return sent.Error;
            }
        }

        // Una cuenta nueva nace con los roles del alta. Una restaurada vuelve con ellos (todos, en la fila de la
        // restauración), cambia los que tenía antes de borrarse y queda con el correo y el número del alta (N4): cada uno
        // que cambió, o que perdió la verificación, deja su fila.
        if (created.Value.Deleted is { } deleted)
        {
            changes.Restored(account.Id, requestedRoles);
            changes.RolesChanged(account.Id, assignment.Added, assignment.Removed);
            changes.ContactChanged(deleted, account);
        }
        else
        {
            changes.RolesChanged(account.Id, requestedRoles, []);
        }

        return new CreateUserResponse(account.Id, created.Value.Restored);
    }

    // La edición, ya validada: la forma de los datos, los locks, la cuenta (leída bajo los locks), el país de un número
    // nuevo, los roles, las guardas y que el correo y el número estén libres; recién entonces las escrituras.
    private async Task<Result> UpdateCoreAsync(UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var emailResult = UserContactLinker.ReadEmail(request.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        // El país solo se comprueba si el número es nuevo; primero hay que leer la cuenta bajo los locks.
        var phoneResult = contacts.ParsePhone(request.Phone);
        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var (email, phone) = (emailResult.Value, phoneResult.Value);

        // La fila de la cuenta, después de los locks de los destinos y del número: dos ediciones, el perfil, la
        // activación o el borrado pasan de a uno, y la revisión se compara con lo que confirmó el anterior (N2).
        var user = await contacts.LockAsync(request.UserId, email, phone, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (user.Revision != request.ExpectedRevision)
        {
            return UserErrors.RevisionConflict;
        }

        var newEmail = email is not null && !string.Equals(email.Value, user.Email, StringComparison.OrdinalIgnoreCase)
            ? email
            : null;
        var newPhone = phone is not null && phone.Value != user.PhoneNumber ? phone : null;
        if (newPhone is not null && await contacts.EnsureCountryAllowedAsync(newPhone, cancellationToken) is { IsFailure: true } country)
        {
            return country.Error;
        }

        IReadOnlyCollection<string> requestedRoles = [.. request.Roles!.Distinct(StringComparer.Ordinal)];
        var rolesExist = await guard.EnsureRolesExistAsync(requestedRoles, cancellationToken);
        if (rolesExist.IsFailure)
        {
            return rolesExist.Error;
        }

        // Antes que los roles, que pueden tomar el lock global de administradores: un rechazo por permisos no hace fila.
        var contactAllowed = await guard.EnsureContactCanChangeAsync(user, newEmail, newPhone, cancellationToken);
        if (contactAllowed.IsFailure)
        {
            return contactAllowed.Error;
        }

        var allowed = await guard.EnsureRolesCanChangeAsync(user, requestedRoles, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var free = await contacts.EnsureFreeAsync(user.Id, newEmail, newPhone, cancellationToken);
        if (free.IsFailure)
        {
            return free.Error;
        }

        await userRepository.SetDisplayNameAsync(user.Id, request.DisplayName, cancellationToken);
        var assignment = await userRepository.SetRolesAsync(user.Id, requestedRoles, cancellationToken);

        // SetRolesAsync busca cada rol por nombre: se vuelve a comprobar con los que la cuenta tiene ahora, por id.
        var assigned = await guard.EnsureAssignedRolesAsync(user, allowed.Value, cancellationToken);
        if (assigned.IsFailure)
        {
            return assigned.Error;
        }

        var changed = await contacts.ChangeAsync(user.Id, newEmail, newPhone, cancellationToken);
        if (changed.IsFailure)
        {
            return changed;
        }

        // Lo que carga un administrador queda sin verificar. Del correo y del número, sólo el estado.
        changes.RolesChanged(user.Id, assignment.Added, assignment.Removed);
        if (newEmail is not null)
        {
            changes.EmailChanged(user, present: true, verified: false);
        }

        if (newPhone is not null)
        {
            changes.PhoneChanged(user, present: true, verified: false);
        }

        return Result.Success();
    }

    private async Task<Result> SendInvitationCoreAsync(SendUserInvitationRequest request, CancellationToken cancellationToken)
    {
        // Dos reenvíos de la misma cuenta pasan de a uno y el segundo ve el guardado del primero.
        var user = await invitations.LockAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (!user.IsActive)
        {
            return UserInvitationErrors.UserInactive;
        }

        var channel = request.Channel!.Value;
        var allowed = invitations.Check(
            channel,
            request.Consent,
            user.DisplayName,
            user.Email is not null,
            user.PhoneNumber is not null,
            InvitationFields.OfResend);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var wait = await invitations.WaitBeforeAnotherAsync(user.Id, cancellationToken);
        if (wait > TimeSpan.Zero)
        {
            return UserInvitationErrors.TooManyRequests((int)Math.Ceiling(wait.TotalSeconds));
        }

        // Toma otra vez el lock de invitaciones de la cuenta (es reentrante) y encola antes del commit.
        return await invitations.SendAsync(user, channel, cancellationToken);
    }
}
