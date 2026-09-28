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
/// el orden de cada caso de uso.
/// </summary>
internal sealed class UserAdministrationService(
    IUserReader users,
    IUserRepository userRepository,
    UserContactLinker contacts,
    UserInvitationIssuer invitations,
    UserGuard guard,
    IRequestValidator validator,
    IUnitOfWork unitOfWork,
    ILogger<UserAdministrationService> logger) : IUserAdministrationService
{
    public Task<Result<Guid>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<Guid>>(logger, "CreateUser", async () =>
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

    // El alta, ya validada: correo, teléfono, que haya al menos uno, las reglas de la invitación y los roles, todo antes
    // de los locks; después, el alta o la restauración, los roles y el envío.
    private async Task<Result<Guid>> CreateCoreAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var emailResult = UserContactLinker.ReadEmail(request.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var phoneResult = contacts.ReadPhone(request.Phone);
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
        var rolesExist = await guard.EnsureRolesExistAsync(requestedRoles, cancellationToken);
        if (rolesExist.IsFailure)
        {
            return rolesExist.Error;
        }

        await contacts.LockDestinationsAsync(email, phone, cancellationToken);
        var account = await contacts.CreateOrRestoreAsync(email, phone, request.DisplayName, cancellationToken);
        if (account.IsFailure)
        {
            return account.Error;
        }

        await userRepository.SetRolesAsync(account.Value.Id, requestedRoles, cancellationToken);
        if (request.Invitation?.Channel is { } requestedChannel)
        {
            // Toma el lock de invitaciones de la cuenta antes de agregar y encolar: la cola de WhatsApp lo pide para
            // encontrar la invitación después del commit.
            await invitations.SendAsync(account.Value, requestedChannel, cancellationToken);
        }

        return account.Value.Id;
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
        await contacts.LockAsync(request.UserId, email, phone, cancellationToken);

        var user = await users.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var newEmail = email is not null && !string.Equals(email.Value, user.Email, StringComparison.OrdinalIgnoreCase)
            ? email
            : null;
        var newPhone = phone is not null && phone.Value != user.PhoneNumber ? phone : null;
        if (newPhone is not null && contacts.EnsureCountryAllowed(newPhone) is { IsFailure: true } country)
        {
            return country.Error;
        }

        IReadOnlyCollection<string> requestedRoles = [.. request.Roles!.Distinct(StringComparer.Ordinal)];
        var rolesExist = await guard.EnsureRolesExistAsync(requestedRoles, cancellationToken);
        if (rolesExist.IsFailure)
        {
            return rolesExist.Error;
        }

        var allowed = await guard.EnsureRolesCanChangeAsync(request.UserId, requestedRoles, cancellationToken);
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
        await userRepository.SetRolesAsync(user.Id, requestedRoles, cancellationToken);
        return await contacts.ChangeAsync(user.Id, newEmail, newPhone, cancellationToken);
    }

    private async Task<Result> SendInvitationCoreAsync(SendUserInvitationRequest request, CancellationToken cancellationToken)
    {
        // Dos reenvíos de la misma cuenta pasan de a uno y el segundo ve el guardado del primero.
        await invitations.LockAsync(request.UserId, cancellationToken);

        var user = await users.FindByIdAsync(request.UserId, cancellationToken);
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
        await invitations.SendAsync(user, channel, cancellationToken);
        return Result.Success();
    }
}
