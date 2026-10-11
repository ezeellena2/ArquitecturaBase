using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using Microsoft.AspNetCore.DataProtection;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;
internal sealed class WhatsAppFlowSessionProtector(IDataProtectionProvider provider) : IWhatsAppFlowSessionProtector
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public byte[] Protect(Guid sessionId, Guid contactId, Guid userId, Guid versionId, WhatsAppFlowSessionPayload payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Options);
        try { return Protector(sessionId, contactId, userId, versionId).Protect(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public WhatsAppFlowSessionPayload Read(Guid sessionId, Guid contactId, Guid userId, Guid versionId, byte[] payload)
    {
        var bytes = Protector(sessionId, contactId, userId, versionId).Unprotect(payload);
        try
        {
            var value = JsonSerializer.Deserialize<WhatsAppFlowSessionPayload>(bytes, Options);
            return value is { Format: 1 } && value.ContactId == contactId && value.UserId == userId && value.VersionId == versionId
                ? value : throw new InvalidOperationException("The private Flow session does not match its owner.");
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private IDataProtector Protector(Guid sessionId, Guid contactId, Guid userId, Guid versionId) => provider.CreateProtector(
        "ArquitecturaBase.WhatsApp.FlowSessions.v1", sessionId.ToString("N"), contactId.ToString("N"), userId.ToString("N"), versionId.ToString("N"));
}
