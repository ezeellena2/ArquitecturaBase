using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using Microsoft.AspNetCore.DataProtection;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;

// What a received document keeps private (task 7): a purpose of its own, versioned and bound to the document and to what the value is
// (its reading, its proposal or a step of its history); the plain text is cleared from memory.
internal sealed class DataProtectionFormReceivedDocumentProtector(IDataProtectionProvider protection) : IFormReceivedDocumentProtector
{
    public byte[] Protect(Guid documentId, string kind, string json)
    {
        var plaintext = Encoding.UTF8.GetBytes(json);
        try { return Protector(documentId, kind).Protect(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public string? Unprotect(Guid documentId, string kind, byte[] protectedValue)
    {
        byte[] plaintext;
        try { plaintext = Protector(documentId, kind).Unprotect(protectedValue); }
        catch (CryptographicException) { return null; }
        try { return Encoding.UTF8.GetString(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private IDataProtector Protector(Guid documentId, string kind) =>
        protection.CreateProtector("ArquitecturaBase.BusinessForms.ReceivedDocuments.v1", documentId.ToString("N"), kind);
}
