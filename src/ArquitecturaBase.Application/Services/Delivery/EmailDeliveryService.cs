using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Delivery;

/// <summary>
/// N5 and N8: records on the invitation what the email worker did with its email, as WhatsAppDeliveryService does for WhatsApp.
/// Each record is its own boundary, under the account's invitation lock, so it waits for whoever is inviting that account; an
/// email without an invitation opens nothing. An invitation that is no longer there is skipped.
/// </summary>
internal sealed class EmailDeliveryService(
    IUserInvitationRepository invitations,
    IUnitOfWork unitOfWork,
    ILogger<EmailDeliveryService> logger) : IEmailDeliveryService
{
    private const string RecordSentOperation = "RecordSentEmail";
    private const string RecordUnsentOperation = "RecordUnsentEmail";
    private const string RecordUncertainOperation = "RecordUncertainEmail";

    public Task<Result> RecordSentAsync(EmailMessage message, string messageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        return RecordAsync(message, RecordSentOperation, invitation => invitation.AttachProviderMessage(messageId), cancellationToken);
    }

    public Task<Result> RecordUnsentAsync(EmailMessage message, CancellationToken cancellationToken) =>
        RecordAsync(message, RecordUnsentOperation, invitation => invitation.MarkSendFailed(), cancellationToken);

    public Task<Result> RecordUncertainAsync(EmailMessage message, CancellationToken cancellationToken) =>
        RecordAsync(message, RecordUncertainOperation, invitation => invitation.MarkSendUncertain(), cancellationToken);

    private Task<Result> RecordAsync(
        EmailMessage message, string operation, Action<UserInvitation> record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Invitation is not { } reference)
        {
            return Task.FromResult(Result.Success());
        }

        return OperationLog.RunAsync<Result>(logger, operation, () =>
            unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await invitations.LockAccountAsync(reference.UserId, ct);
                if (await invitations.GetByIdAsync(reference.InvitationId, ct) is { } invitation) record(invitation);

                return Result.Success();
            }, CommitPolicy.OnSuccess, cancellationToken));
    }
}
