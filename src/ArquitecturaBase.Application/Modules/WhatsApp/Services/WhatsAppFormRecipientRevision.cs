using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal static class WhatsAppFormRecipientRevision
{
    public static string Create(Guid actor, WhatsAppFormRecipientRow recipient)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Format = 1, Actor = actor, recipient.ContactId, recipient.UserId, recipient.LinkRevision,
            recipient.WaId, recipient.UserIdentifier, recipient.IsActive, recipient.Culture, recipient.AccountStamp, recipient.RoleFingerprint,
        });
        try { return "v1." + Convert.ToHexStringLower(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
