using System.Text.Json;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Infrastructure.Delivery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Emails;

internal sealed class EmailMessageSource(IServiceScopeFactory scopeFactory, IDataProtectionProvider protection, ILogger<EmailMessageSource> logger)
    : DurableMessageSource<EmailMessage>(scopeFactory, protection, logger)
{
    protected override string Channel => "email";
    protected override EmailMessage Deserialize(string messageType, string payload) => messageType == nameof(EmailMessage)
        ? JsonSerializer.Deserialize<EmailMessage>(payload) ?? throw new InvalidOperationException("A delivery has no content.")
        : throw new InvalidOperationException("The email message type is not supported.");
}
