using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>
/// N5 and N8: what the email worker records of an email it sent, could not send or may have sent. Today only an invitation
/// changes (<see cref="EmailMessage.Invitation"/>); any other email records nothing. Each call is its own boundary, after the
/// call to the mail server, which goes outside any transaction.
/// </summary>
public interface IEmailDeliveryService
{
    /// <summary>The server accepted it: the invitation keeps the Message-ID it went out with, which says it was sent.</summary>
    Task<Result> RecordSentAsync(EmailMessage message, string messageId, CancellationToken cancellationToken);

    /// <summary>It did not go out (refused for good, or expired in the queue): the invitation is failed.</summary>
    Task<Result> RecordUnsentAsync(EmailMessage message, CancellationToken cancellationToken);

    /// <summary>It may have arrived (cut while the body went out): the invitation is uncertain, and it is not sent again.</summary>
    Task<Result> RecordUncertainAsync(EmailMessage message, CancellationToken cancellationToken);
}
