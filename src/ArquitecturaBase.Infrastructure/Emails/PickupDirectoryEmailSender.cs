using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Emails;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>Guarda cada email como .eml para abrirlo con cualquier cliente de correo. Solo para desarrollo.</summary>
internal sealed partial class PickupDirectoryEmailSender(
    IOptions<EmailOptions> emailOptions,
    IOptions<SmtpOptions> smtpOptions,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<PickupDirectoryEmailSender> logger)
    : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(environment.ContentRootPath, emailOptions.Value.PickupDirectory);
        Directory.CreateDirectory(directory);

        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"{timeProvider.GetUtcNow():yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.eml");
        var path = Path.Combine(directory, fileName);

        using var mime = MimeMessageFactory.Create(message, smtpOptions.Value);
        await mime.WriteToAsync(path, cancellationToken);

        LogEmailSaved(logger, path);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email saved to {Path}")]
    private static partial void LogEmailSaved(ILogger logger, string path);
}
