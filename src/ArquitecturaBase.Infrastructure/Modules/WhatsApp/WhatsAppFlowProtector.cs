using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using Microsoft.AspNetCore.DataProtection;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

internal sealed class WhatsAppFlowProtector(IDataProtectionProvider provider) : IWhatsAppFlowProtector
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        { MaxDepth = 32, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public byte[] ProtectManifest(Guid operationId, Guid neutralOperationId, Guid versionId, WhatsAppFlowStoredManifest manifest) =>
        Protect(ManifestProtector(operationId, neutralOperationId, versionId), manifest);
    public WhatsAppFlowStoredManifest ReadManifest(Guid operationId, Guid neutralOperationId, Guid versionId, byte[] protectedManifest)
    {
        var result = Read<WhatsAppFlowStoredManifest>(ManifestProtector(operationId, neutralOperationId, versionId), protectedManifest);
        return result.Format is 1 or 2 ? result : throw new InvalidOperationException("The Flow manifest format is unsupported.");
    }
    public byte[] ProtectProjection(Guid operationId, Guid bindingId, Guid versionId, string culture, WhatsAppFlowProjection projection) =>
        Protect(ProjectionProtector(operationId, bindingId, versionId, culture), projection);
    public WhatsAppFlowProjection ReadProjection(Guid operationId, Guid bindingId, Guid versionId, string culture, byte[] protectedProjection)
    {
        var result = Read<WhatsAppFlowProjection>(ProjectionProtector(operationId, bindingId, versionId, culture), protectedProjection);
        return result.Format == 1 && result.Culture == culture ? result : throw new InvalidOperationException("The Flow projection does not match its owner.");
    }
    private IDataProtector ManifestProtector(Guid operationId, Guid neutralOperationId, Guid versionId) => provider.CreateProtector(
        "ArquitecturaBase.WhatsApp.FlowManifests.v1", operationId.ToString("N"), neutralOperationId.ToString("N"), versionId.ToString("N"));
    private IDataProtector ProjectionProtector(Guid operationId, Guid bindingId, Guid versionId, string culture) => provider.CreateProtector(
        "ArquitecturaBase.WhatsApp.FlowProjections.v1", operationId.ToString("N"), bindingId.ToString("N"), versionId.ToString("N"), culture);
    private static byte[] Protect<T>(IDataProtector protector, T value)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        try { return protector.Protect(plaintext); } finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    private static T Read<T>(IDataProtector protector, byte[] ciphertext)
    {
        var plaintext = protector.Unprotect(ciphertext);
        try { return JsonSerializer.Deserialize<T>(plaintext, Options) ?? throw new InvalidOperationException("The protected Flow payload is invalid."); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}
