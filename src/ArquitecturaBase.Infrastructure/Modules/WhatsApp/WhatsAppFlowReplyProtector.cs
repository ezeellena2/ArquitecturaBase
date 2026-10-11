using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using Microsoft.AspNetCore.DataProtection;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

internal sealed class WhatsAppFlowReplyProtector(IDataProtectionProvider provider) : IWhatsAppFlowReplyProtector
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public byte[] Protect(string waMessageId, Guid contactId, string responseJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waMessageId);
        ArgumentOutOfRangeException.ThrowIfEqual(contactId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(responseJson);
        var bytes = StrictUtf8.GetBytes(responseJson);
        try
        {
            return Protector(waMessageId, contactId).Protect(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string Read(string waMessageId, Guid contactId, byte[] protectedResponse)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waMessageId);
        ArgumentOutOfRangeException.ThrowIfEqual(contactId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(protectedResponse);
        var bytes = Protector(waMessageId, contactId).Unprotect(protectedResponse);
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private IDataProtector Protector(string waMessageId, Guid contactId) => provider.CreateProtector(
        "ArquitecturaBase.WhatsApp.FlowReplies.v1", waMessageId, contactId.ToString("N"));
}
