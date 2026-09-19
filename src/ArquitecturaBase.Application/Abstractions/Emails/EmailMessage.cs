namespace ArquitecturaBase.Application.Abstractions.Emails;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);
