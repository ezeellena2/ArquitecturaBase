namespace ArquitecturaBase.Application.Models.Emails;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);
