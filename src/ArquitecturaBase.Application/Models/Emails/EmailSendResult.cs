namespace ArquitecturaBase.Application.Models.Emails;

/// <summary>
/// N5 and N8: how one attempt to send an email ended, as the provider's adapter tells it. Only what the server did not take, for a
/// reason that may pass, is retried (<see cref="EmailSendResult.IsRetryable"/>): what failed before the body, and a 4xx after it,
/// which says it was not taken (RFC 5321). What may have arrived is never repeated.
/// </summary>
public enum EmailSendOutcome
{
    /// <summary>The server accepted it.</summary>
    Sent,

    /// <summary>The host stopped before the body left: nothing went out, and the email goes back to the queue for another copy.</summary>
    Stopped,

    /// <summary>The server could not be reached, or the connection, the TLS or its greeting failed before the body. Retried.</summary>
    ConnectionFailed,

    /// <summary>The server rejected the credentials (a 535). Retried, and the email health check is degraded at once.</summary>
    AuthenticationFailed,

    /// <summary>The server refused it for now (a 4xx), also after the body. Retried.</summary>
    TemporarilyRejected,

    /// <summary>The server refused it for good (a 5xx), or the email cannot be sent as it is. Not retried.</summary>
    Rejected,

    /// <summary>
    /// The server refused the sender for good (a 5xx to MAIL FROM, as Gmail's 550 5.4.5 once the daily quota is spent): every email
    /// would fail the same for now. Not retried, and the email health check counts it as a failure of the server.
    /// </summary>
    SenderRejected,

    /// <summary>The connection was cut, or the deadline passed, while the body was going out: it may have arrived. Not repeated.</summary>
    Uncertain,
}

/// <summary>One attempt: its outcome, the Message-ID it went out with and the server's status code when it refused it.</summary>
public sealed record EmailSendResult(EmailSendOutcome Outcome)
{
    /// <summary>The Message-ID of the email the server accepted; null otherwise.</summary>
    public string? MessageId { get; init; }

    /// <summary>The SMTP status code of the refusal, when the server gave one (421, 535, 550…), for the logs.</summary>
    public int? StatusCode { get; init; }

    /// <summary>What the server did not take, for a reason that may pass: the connection, the credentials or a 4xx.</summary>
    public bool IsRetryable => Outcome is EmailSendOutcome.ConnectionFailed or EmailSendOutcome.AuthenticationFailed
        or EmailSendOutcome.TemporarilyRejected;

    public static EmailSendResult Sent(string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        return new EmailSendResult(EmailSendOutcome.Sent) { MessageId = messageId };
    }

    public static EmailSendResult Failed(EmailSendOutcome outcome, int? statusCode = null)
    {
        if (outcome == EmailSendOutcome.Sent) throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A sent email has its Message-ID.");

        return new EmailSendResult(outcome) { StatusCode = statusCode };
    }
}
