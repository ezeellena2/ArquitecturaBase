using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Coordina el alta y la edición administrativa sobre IUserRepository. Los locks preceden las escrituras Identity,
/// que autoguardan en la transacción compartida; IUnitOfWork confirma solo al terminar el caso de uso correctamente.
/// </summary>
internal sealed class UserWriteOperations(
    IUserRepository userRepository,
    IUserReader users,
    IRoleReader roles,
    ILoginCodeRepository destinations,
    UserContactParser contacts,
    UserInvitationSender invitationSender,
    UserGuards guards,
    PhoneNumberChange phoneChange,
    WhatsAppContactLinker contactLinker,
    ServiceRequestValidator<CreateUserRequest> createValidator,
    ServiceRequestValidator<UpdateUserRequest> updateValidator,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<Guid>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (await createValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            return validationError;
        }

        var result = await CreateCoreAsync(request, cancellationToken);
        if (result.IsSuccess)
        {
            // El decorator anterior confirma solo al terminar bien; Identity puede haber autoguardado dentro del lock.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public async Task<Result> UpdateAsync(UpdateUserRequest request, CancellationToken cancellationToken)
    {
        if (await updateValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            return validationError;
        }

        var result = await UpdateCoreAsync(request, cancellationToken);
        if (result.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    private async Task<Result<Guid>> CreateCoreAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var emailResult = UserContactParser.ReadEmail(request.Email);
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
            var allowed = invitationSender.Check(
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
        var knownRoles = await roles.ListRoleNamesAsync(cancellationToken);
        if (requestedRoles.Any(role => !knownRoles.Contains(role, StringComparer.Ordinal)))
        {
            return RoleErrors.NotFound;
        }

        // Correo primero y teléfono después, igual que el alta anterior y el ingreso con código.
        await LockDestinationsAsync(email, phone, cancellationToken);
        var account = await CreateOrRestoreAsync(email, phone, request.DisplayName, cancellationToken);
        if (account.IsFailure)
        {
            return account.Error;
        }

        await userRepository.SetRolesAsync(account.Value.Id, requestedRoles, cancellationToken);
        if (request.Invitation?.Channel is { } requestedChannel)
        {
            await invitationSender.SendAsync(account.Value, requestedChannel, cancellationToken);
        }

        return account.Value.Id;
    }

    private async Task<Result> UpdateCoreAsync(UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var emailResult = UserContactParser.ReadEmail(request.Email);
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
        await LockForUpdateAsync(request.UserId, email, phone, cancellationToken);

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
        var knownRoles = await roles.ListRoleNamesAsync(cancellationToken);
        if (requestedRoles.Any(role => !knownRoles.Contains(role, StringComparer.Ordinal)))
        {
            return RoleErrors.NotFound;
        }

        var allowed = await guards.EnsureRolesCanChangeAsync(request.UserId, requestedRoles, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var free = await EnsureFreeAsync(user.Id, newEmail, newPhone, cancellationToken);
        if (free.IsFailure)
        {
            return free.Error;
        }

        await userRepository.SetDisplayNameAsync(user.Id, request.DisplayName, cancellationToken);
        await userRepository.SetRolesAsync(user.Id, requestedRoles, cancellationToken);
        return await ChangeContactAsync(user.Id, newEmail, newPhone, cancellationToken);
    }

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

    private async Task LockForUpdateAsync(Guid userId, Email? email, PhoneNumber? phone, CancellationToken cancellationToken)
    {
        await LockDestinationsAsync(email, phone, cancellationToken);
        if (phone is not null)
        {
            // Contactos antes que cuenta: el mismo orden del bot y del perfil.
            await phoneChange.LockAsync(userId, phone, cancellationToken);
        }
    }

    private async Task<Result<UserAccount>> CreateOrRestoreAsync(
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
            || await users.IsDeletedEmailAsync(email, cancellationToken);
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

    private async Task<Result> EnsureFreeAsync(
        Guid userId, Email? email, PhoneNumber? phone, CancellationToken cancellationToken)
    {
        if (email is not null
            && (await users.FindByEmailAsync(email, cancellationToken) is { } emailOwner
                ? emailOwner.Id != userId
                : await users.IsDeletedEmailAsync(email, cancellationToken)))
        {
            return UserErrors.AlreadyExists;
        }

        if (phone is not null
            && (await users.FindByPhoneAsync(phone, cancellationToken) is { } phoneOwner
                ? phoneOwner.Id != userId
                : await users.IsDeletedPhoneAsync(phone, cancellationToken)))
        {
            return UserErrors.PhoneAlreadyExists;
        }

        return Result.Success();
    }

    private async Task<Result> ChangeContactAsync(
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

            await contactLinker.UnlinkUserAsync(userId, cancellationToken);
            await phoneChange.VoidPendingLinksAsync(userId, cancellationToken);
        }

        return Result.Success();
    }
}
