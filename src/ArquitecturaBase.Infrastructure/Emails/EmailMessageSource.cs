using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Infrastructure.Delivery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Emails;

internal sealed class EmailMessageSource(IServiceScopeFactory scopeFactory, IDataProtectionProvider protection, ILogger<EmailMessageSource> logger)
    : DurableMessageSource<EmailMessage>(scopeFactory, protection, logger)
{
    // N27 and H25: a member this version does not know (an attachment a newer one adds) makes it unreadable here, so it is left
    // for another copy instead of going out without it.
    private static readonly JsonSerializerOptions Strict = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly string[] Types = [nameof(EmailMessage)];

    protected override string Channel => "email";
    protected override IReadOnlyCollection<string> MessageTypes => Types;
    protected override EmailMessage Deserialize(string messageType, string payload) => messageType == nameof(EmailMessage)
        ? JsonSerializer.Deserialize<EmailMessage>(payload, Strict) ?? throw new InvalidOperationException("A delivery has no content.")
        : throw new InvalidOperationException("The email message type is not supported.");
}
