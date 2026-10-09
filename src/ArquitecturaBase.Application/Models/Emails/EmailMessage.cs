namespace ArquitecturaBase.Application.Models.Emails;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody, DateTime? ExpiresAtUtc = null)
{
    public override string ToString() => nameof(EmailMessage);
}
