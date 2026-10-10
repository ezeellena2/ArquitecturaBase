using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

internal sealed class WhatsAppFlowEnvironment(IOptions<WhatsAppOptions> options, IWhatsAppFlowProvider client) : IWhatsAppFlowEnvironment
{
    public bool IsConfigured => NumericId(options.Value.WabaId) && NumericId(options.Value.PhoneNumberId)
        && !string.IsNullOrWhiteSpace(options.Value.AccessToken) && options.Value.GraphApiVersion == WhatsAppFlowProfile.GraphApiVersion;
    public async Task<Result<WhatsAppFlowAccountContext>> VerifyAccountAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured) return FlowErrors.AccountUnavailable;
        var settings = options.Value;
        if (!await client.VerifyPhoneAsync(settings.WabaId!, settings.PhoneNumberId!, cancellationToken)) return FlowErrors.AccountMismatch;
        return new WhatsAppFlowAccountContext(settings.WabaId!, settings.PhoneNumberId!, settings.GraphApiVersion, Fingerprint(settings));
    }
    internal static string Fingerprint(WhatsAppOptions settings)
    {
        var bytes = Encoding.UTF8.GetBytes($"{settings.WabaId}\n{settings.PhoneNumberId}\n{settings.GraphApiVersion}\n{settings.AccessToken}");
        try { return Convert.ToHexStringLower(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static bool NumericId(string? value) => value is { Length: > 0 and <= 64 } && value.All(character => character is >= '0' and <= '9');
}
