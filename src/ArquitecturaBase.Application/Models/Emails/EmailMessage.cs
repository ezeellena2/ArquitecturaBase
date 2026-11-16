namespace ArquitecturaBase.Application.Models.Emails;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody, DateTime? ExpiresAtUtc = null)
{
    /// <summary>
    /// N5 and O3: names this email in the logs, from the queue that takes it (or not) to the worker that sends it, without its
    /// address or its content.
    /// </summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>
    /// N5: the invitation this email carries, so the worker records whether it went out, did not or may have arrived; absent in
    /// the rest, so their queued payloads keep their members.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public EmailInvitationReference? Invitation { get; init; }

    /// <summary>Files that go attached, such as the PDF of a copy; absent in the rest, so their queued payloads keep their bytes.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<EmailAttachment>? Attachments { get; init; }

    public override string ToString() => nameof(EmailMessage);
}

/// <summary>N5: the account and the invitation an email belongs to: ids only, never the address.</summary>
public sealed record EmailInvitationReference(Guid UserId, Guid InvitationId);

/// <summary>A file attached to an email: its name, its media type and its bytes, which travel in the encrypted queued payload.</summary>
public sealed record EmailAttachment(string Name, string ContentType, byte[] Content)
{
    public override string ToString() => nameof(EmailAttachment);
}
