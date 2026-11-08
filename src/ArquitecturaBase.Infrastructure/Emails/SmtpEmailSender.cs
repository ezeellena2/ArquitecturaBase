using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Models.Emails;
using MailKit.Net.Smtp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options, TimeSpan timeout) : IEmailSender
{
    // MailKit waits two minutes by default; the email worker sends one message at a time, so a silent server would hold the queue.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    [ActivatorUtilitiesConstructor]
    public SmtpEmailSender(IOptions<SmtpOptions> options) : this(options, DefaultTimeout)
    {
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var mime = MimeMessageFactory.Create(message, settings);
        using var client = new SmtpClient { Timeout = (int)timeout.TotalMilliseconds };

        await client.ConnectAsync(settings.Host, settings.Port, settings.Security, cancellationToken);
        await client.AuthenticateAsync(settings.UserName, settings.Password, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
