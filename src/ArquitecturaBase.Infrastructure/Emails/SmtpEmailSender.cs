using ArquitecturaBase.Application.Abstractions.Emails;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var mime = MimeMessageFactory.Create(message, settings);
        using var client = new SmtpClient();

        await client.ConnectAsync(settings.Host, settings.Port, settings.Security, cancellationToken);
        await client.AuthenticateAsync(settings.UserName, settings.Password, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
