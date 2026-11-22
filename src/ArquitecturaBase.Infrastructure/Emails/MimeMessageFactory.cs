using ArquitecturaBase.Application.Models.Emails;
using MimeKit;

namespace ArquitecturaBase.Infrastructure.Emails;

internal static class MimeMessageFactory
{
    /// <summary><paramref name="fromName"/> is the sender's name from Configuración › Apariencia (RC6-B), read for this email.</summary>
    public static MimeMessage Create(EmailMessage message, string fromName, string fromAddress)
    {
        // N5: the Message-ID is what an invitation keeps when the server accepts it, so every email has one (MimeKit makes it).
        var mime = new MimeMessage();
        if (string.IsNullOrEmpty(mime.MessageId)) mime.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId();
        mime.From.Add(new MailboxAddress(fromName, fromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        var body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody };
        foreach (var attachment in message.Attachments ?? [])
            body.Attachments.Add(attachment.Name, attachment.Content, ContentType.Parse(attachment.ContentType));
        mime.Body = body.ToMessageBody();

        return mime;
    }
}
