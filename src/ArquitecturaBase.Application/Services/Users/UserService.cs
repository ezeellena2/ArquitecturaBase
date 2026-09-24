using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Users;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Users;

internal sealed partial class UserService(
    IUserReader userReader,
    IUserInvitationRepository invitations,
    IWhatsAppMessageRepository messages,
    IPhoneNumberParser phoneNumbers,
    ServiceRequestValidator<ListUsersRequest> listValidator,
    ServiceRequestValidator<UserFilterCountsRequest> countsValidator,
    UserWriteOperations writes,
    UserInvitationSender invitationSender,
    ServiceRequestValidator<SendUserInvitationRequest> invitationValidator,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    UserStatusOperations status,
    UserPhoneOperations phone,
    ILogger<UserService> logger) : IUserService
{
    private const string ListOperation = "GetUsersQuery";
    private const string CountsOperation = "GetUserFilterCountsQuery";
    private const string GetOperation = "GetUserQuery";

    public async Task<Result<PagedResult<UserListItem>>> ListUsersAsync(
        ListUsersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, ListOperation);

        var validationError = await listValidator.ValidateAsync(request, cancellationToken);
        if (validationError is not null)
        {
            LogFailed(logger, ListOperation, validationError.Code);
            return validationError;
        }

        var page = await userReader.ListUsersAsync(request, cancellationToken);
        var formatted = new PagedResult<UserListItem>(
            [.. page.Items.Select(ToListItem)], page.Page, page.PageSize, page.TotalCount);

        LogHandled(logger, ListOperation);
        return formatted;
    }

    public async Task<Result<UserFilterCounts>> GetUserFilterCountsAsync(
        UserFilterCountsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, CountsOperation);

        var validationError = await countsValidator.ValidateAsync(request, cancellationToken);
        if (validationError is not null)
        {
            LogFailed(logger, CountsOperation, validationError.Code);
            return validationError;
        }

        var counts = await userReader.GetUserFilterCountsAsync(request, cancellationToken);

        LogHandled(logger, CountsOperation);
        return counts;
    }

    public async Task<Result<UserDetail>> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        LogHandling(logger, GetOperation);

        if (await userReader.FindDetailAsync(userId, cancellationToken) is not { } detail)
        {
            LogFailed(logger, GetOperation, UserErrors.NotFoundCode);
            return UserErrors.NotFound;
        }

        // Sin número, Create falla y queda en null, igual que el número.
        var phone = PhoneNumber.Create(detail.PhoneNumber);
        var result = new UserDetail(
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
            LastInvitation = await LastInvitationAsync(detail.Id, cancellationToken),
        };

        LogHandled(logger, GetOperation);
        return result;
    }

    public async Task<Result<Guid>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string operation = "CreateUserCommand";
        LogHandling(logger, operation);
        var result = await writes.CreateAsync(request, cancellationToken);
        LogOutcome(logger, operation, result);
        return result;
    }

    public async Task<Result> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string operation = "UpdateUserCommand";
        LogHandling(logger, operation);
        var result = await writes.UpdateAsync(request, cancellationToken);
        LogOutcome(logger, operation, result);
        return result;
    }

    public async Task<Result> SendInvitationAsync(SendUserInvitationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string operation = "SendInvitationCommand";
        LogHandling(logger, operation);

        if (await invitationValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogFailed(logger, operation, validationError.Code);
            return validationError;
        }

        // Dos reenvíos de la misma cuenta pasan de a uno y el segundo ve el guardado del primero.
        await invitations.LockAccountAsync(request.UserId, cancellationToken);

        var user = await userReader.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            LogFailed(logger, operation, UserErrors.NotFoundCode);
            return UserErrors.NotFound;
        }

        if (!user.IsActive)
        {
            LogFailed(logger, operation, UserInvitationErrors.UserInactiveCode);
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
            LogFailed(logger, operation, allowed.Error.Code);
            return allowed.Error;
        }

        var wait = (await invitations.GetLatestSentAsync(user.Id, cancellationToken))
            ?.WaitBeforeAnother(timeProvider.GetUtcNow().UtcDateTime) ?? TimeSpan.Zero;
        if (wait > TimeSpan.Zero)
        {
            var error = UserInvitationErrors.TooManyRequests((int)Math.Ceiling(wait.TotalSeconds));
            LogFailed(logger, operation, error.Code);
            return error;
        }

        await invitationSender.SendAsync(user, channel, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        LogHandled(logger, operation);
        return Result.Success();
    }

    public async Task<Result> SetUserActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken)
    {
        const string operation = "SetUserActiveCommand";
        LogHandling(logger, operation);
        var result = await status.SetActiveAsync(userId, isActive, cancellationToken);
        LogOutcome(logger, operation, result);
        return result;
    }

    public async Task<Result> DeleteUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string operation = "DeleteUserCommand";
        LogHandling(logger, operation);
        var result = await status.DeleteAsync(userId, cancellationToken);
        LogOutcome(logger, operation, result);
        return result;
    }

    public async Task<Result> UnlinkUserPhoneAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string operation = "UnlinkUserPhoneCommand";
        LogHandling(logger, operation);
        var result = await phone.UnlinkAsync(userId, cancellationToken);
        LogOutcome(logger, operation, result);
        return result;
    }

    private static void LogOutcome(ILogger logger, string operation, Result result)
    {
        if (result.IsSuccess)
        {
            LogHandled(logger, operation);
        }
        else
        {
            LogFailed(logger, operation, result.Error.Code);
        }
    }

    // Sin número, Create falla y queda en null, igual que el número.
    private UserListItem ToListItem(ArquitecturaBase.Application.Features.Users.GetUsers.UserListItem item)
    {
        var phone = PhoneNumber.Create(item.PhoneNumber);

        return new UserListItem(
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

    private async Task<LastInvitation?> LastInvitationAsync(Guid userId, CancellationToken cancellationToken) =>
        await invitations.GetLatestAsync(userId, cancellationToken) is { } invitation
            ? new LastInvitation(invitation.Channel, invitation.SentAtUtc, await DeliveryStatusAsync(invitation, cancellationToken))
            : null;

    /// <summary>Por correo no hay estado. Por WhatsApp, se proyecta el último estado de entrega registrado.</summary>
    private async Task<InvitationDeliveryStatus?> DeliveryStatusAsync(UserInvitation invitation, CancellationToken cancellationToken)
    {
        if (invitation.Channel is not UserInvitationChannel.WhatsApp)
        {
            return null;
        }

        if (invitation.SendFailed)
        {
            return InvitationDeliveryStatus.Failed;
        }

        if (invitation.WaMessageId is not { } waMessageId)
        {
            return InvitationDeliveryStatus.Pending;
        }

        var sent = (await messages.ListOutboundAsync([waMessageId], cancellationToken)).SingleOrDefault();

        return sent?.Status switch
        {
            WhatsAppMessageStatus.Sent => InvitationDeliveryStatus.Sent,
            WhatsAppMessageStatus.Delivered => InvitationDeliveryStatus.Delivered,
            WhatsAppMessageStatus.Read => InvitationDeliveryStatus.Read,
            WhatsAppMessageStatus.Failed => InvitationDeliveryStatus.Failed,
            _ => InvitationDeliveryStatus.Pending,
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Operation}")]
    private static partial void LogHandling(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Operation}")]
    private static partial void LogHandled(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Operation} failed with {ErrorCode}")]
    private static partial void LogFailed(ILogger logger, string operation, string errorCode);
}
