namespace ArquitecturaBase.Application.Models.Emails;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody, DateTime? ExpiresAtUtc = null)
{
    /// <summary>Files that go attached, such as the PDF of a copy; absent in the rest, so their queued payloads keep their bytes.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<EmailAttachment>? Attachments { get; init; }

    public override string ToString() => nameof(EmailMessage);
}

/// <summary>A file attached to an email: its name, its media type and its bytes, which travel in the encrypted queued payload.</summary>
public sealed record EmailAttachment(string Name, string ContentType, byte[] Content)
{
    public override string ToString() => nameof(EmailAttachment);
}
