using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Users;

internal sealed class UserService(
    IUserReader userReader,
    IUserInvitationRepository invitations,
    IUserInvitationReader invitationReader,
    IPhoneNumberParser phoneNumbers,
    IRequestValidator validator,
    UserWriteOperations writes,
    UserInvitationSender invitationSender,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    UserStatusOperations status,
    UserPhoneOperations phone,
    ILogger<UserService> logger) : IUserService
{
    public Task<Result<PagedResult<UserListItemResponse>>> ListUsersAsync(
        ListUsersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<PagedResult<UserListItemResponse>>>(logger, "ListUsers", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            var page = await userReader.ListUsersAsync(request, cancellationToken);
            return new PagedResult<UserListItemResponse>(
                [.. page.Items.Select(ToListItem)], page.Page, page.PageSize, page.TotalCount);
        });
    }

    public Task<Result<UserFilterCounts>> GetUserFilterCountsAsync(
        ListUsersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<UserFilterCounts>>(logger, "GetUserFilterCounts", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            return await userReader.CountByFilterOptionAsync(request, cancellationToken);
        });
    }

    public Task<Result<UserDetailResponse>> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<UserDetailResponse>>(logger, "GetUser", async () =>
        {
            if (await userReader.FindDetailAsync(userId, cancellationToken) is not { } detail)
            {
                return UserErrors.NotFound;
            }

            // Sin número, Create falla y queda en null, igual que el número.
            var phone = PhoneNumber.Create(detail.PhoneNumber);
            return new UserDetailResponse(
                detail.Id,
                detail.Email,
                detail.EmailConfirmed,
                detail.PhoneNumber,
                detail.PhoneNumberConfirmed,
                detail.DisplayName,
                detail.IsActive,
                detail.CreatedAtUtc,
                detail.Roles)
            {
                FormattedPhoneNumber = phone.IsSuccess ? phoneNumbers.FormatInternational(phone.Value) : null,
                LastInvitation = await invitationReader.FindLatestAsync(detail.Id, cancellationToken) is { } invitation
                    ? LastInvitation.From(invitation)
                    : null,
            };
        });

    public Task<Result<Guid>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<Guid>>(logger, "CreateUser", async () =>
        {
            if (await writes.ValidateCreateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            // Identity autoguarda la cuenta, sus roles y la restauración adentro: un error de negocio deshace todo. La
            // invitación se encola antes del commit, así la fila queda guardada ya con su estado.
            return await unitOfWork.ExecuteInTransactionAsync(
                ct => writes.CreateAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }

    public Task<Result> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result>(logger, "UpdateUser", async () =>
        {
            if (await writes.ValidateUpdateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            // El nombre, los roles, el correo y el número se autoguardan por separado: adentro del límite quedan todos o
            // ninguno, también cuando no se toca el correo ni el número y no se toma ningún lock.
            return await unitOfWork.ExecuteInTransactionAsync(
                ct => writes.UpdateAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
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

    public Task<Result> SetUserActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "SetUserActive", () =>
            // Desactivar autoguarda el estado y revoca todo el acceso ya emitido (UPDATE inmediatos de OpenIddict): o
            // pasa todo o no pasa nada. Activar es una sola escritura, y va igual dentro del límite: todo método que
            // escribe abre uno.
            unitOfWork.ExecuteInTransactionAsync(
                ct => status.SetActiveAsync(userId, isActive, ct), CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result> DeleteUserAsync(Guid userId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "DeleteUser", () =>
            unitOfWork.ExecuteInTransactionAsync(
                ct => status.DeleteAsync(userId, ct), CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result> UnlinkUserPhoneAsync(Guid userId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "UnlinkUserPhone", () =>
            // Una cuenta que ya no tiene número también es un éxito: suelta un contacto que haya quedado y sus enlaces.
            unitOfWork.ExecuteInTransactionAsync(
                ct => phone.UnlinkAsync(userId, ct), CommitPolicy.OnSuccess, cancellationToken));

    private async Task<Result> SendInvitationCoreAsync(SendUserInvitationRequest request, CancellationToken cancellationToken)
    {
        // Dos reenvíos de la misma cuenta pasan de a uno y el segundo ve el guardado del primero.
        await invitations.LockAccountAsync(request.UserId, cancellationToken);

        var user = await userReader.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (!user.IsActive)
        {
            return UserInvitationErrors.UserInactive;
        }

        var channel = request.Channel!.Value;
        var allowed = invitationSender.Check(
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

        var wait = (await invitations.GetLatestSentAsync(user.Id, cancellationToken))
            ?.WaitBeforeAnother(timeProvider.GetUtcNow().UtcDateTime) ?? TimeSpan.Zero;
        if (wait > TimeSpan.Zero)
        {
            return UserInvitationErrors.TooManyRequests((int)Math.Ceiling(wait.TotalSeconds));
        }

        // Toma otra vez el lock de invitaciones de la cuenta (es reentrante) y encola antes del commit.
        await invitationSender.SendAsync(user, channel, cancellationToken);
        return Result.Success();
    }

    // Sin número, Create falla y queda en null, igual que el número.
    private UserListItemResponse ToListItem(UserListRow item)
    {
        var phone = PhoneNumber.Create(item.PhoneNumber);

        return new UserListItemResponse(
            item.Id,
            item.Email,
            item.PhoneNumber,
            item.PhoneNumberConfirmed,
            item.DisplayName,
            item.IsActive,
            item.CreatedAtUtc,
            item.Roles)
        {
            FormattedPhoneNumber = phone.IsSuccess ? phoneNumbers.FormatInternational(phone.Value) : null,
        };
    }
}
