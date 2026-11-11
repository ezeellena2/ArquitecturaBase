using ArquitecturaBase.Application.Models.Emails;
using MimeKit;

namespace ArquitecturaBase.Infrastructure.Emails;

internal static class MimeMessageFactory
{
    public static MimeMessage Create(EmailMessage message, SmtpOptions sender)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(sender.FromName, sender.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        var body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody };
        foreach (var attachment in message.Attachments ?? [])
            body.Attachments.Add(attachment.Name, attachment.Content, ContentType.Parse(attachment.ContentType));
        mime.Body = body.ToMessageBody();

        return mime;
    }
}
