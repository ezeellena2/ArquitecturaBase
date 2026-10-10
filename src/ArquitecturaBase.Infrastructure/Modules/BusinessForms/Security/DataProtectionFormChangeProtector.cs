using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.AspNetCore.DataProtection;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
internal sealed class DataProtectionFormChangeProtector(IDataProtectionProvider protection) : IFormChangeProtector
{
    // This JSON is encrypted binary data, never embedded in HTML. UTF-8 avoids inflating valid localized drafts.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = 32,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    public byte[] Protect(FormChangeProtectionContext context, string canonicalRequest, FormDraftCommitResponse response)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new FormProtectedChange(Fingerprint(canonicalRequest), response), Options);
        try { return Protector(context).Protect(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    public FormProtectedChange Unprotect(FormChangeProtectionContext context, byte[] protectedResult)
    {
        if (context.Format != 1) throw new InvalidOperationException("The form result format is unsupported.");
        var plaintext = Protector(context).Unprotect(protectedResult);
        try
        {
            var value = JsonSerializer.Deserialize<FormProtectedChange>(plaintext, Options) ?? throw new InvalidOperationException("The protected form result is invalid.");
            if (value.Fingerprint.Length != 32 || value.Result.RequestId != context.RequestId || value.Result.Definition.Id != context.DefinitionId)
                throw new InvalidOperationException("The protected form result does not match its owner.");
            return value;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    public bool Matches(FormProtectedChange stored, string canonicalRequest)
    {
        var fingerprint = Fingerprint(canonicalRequest);
        try { return CryptographicOperations.FixedTimeEquals(stored.Fingerprint, fingerprint); }
        finally { CryptographicOperations.ZeroMemory(fingerprint); }
    }
    private static byte[] Fingerprint(string canonicalRequest)
    {
        var bytes = Encoding.UTF8.GetBytes(canonicalRequest);
        try { return SHA256.HashData(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private IDataProtector Protector(FormChangeProtectionContext context) => protection.CreateProtector("ArquitecturaBase.BusinessForms.DraftChanges.v1",
        context.Id.ToString("N"), context.Actor.ToString("N"), context.RequestId.ToString("N"), context.DefinitionId.ToString("N"), context.Kind);
}
