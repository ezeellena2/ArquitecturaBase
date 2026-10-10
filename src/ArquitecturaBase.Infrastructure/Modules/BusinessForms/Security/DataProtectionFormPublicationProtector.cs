using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.AspNetCore.DataProtection;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;

internal sealed class DataProtectionFormPublicationProtector(IDataProtectionProvider provider) : IFormPublicationProtector
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        { MaxDepth = 32, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public string ProtectTicket(FormPublicationTicket ticket) => Convert.ToBase64String(Protect(TicketProtector(), ticket));
    public FormPublicationTicket? ReadTicket(string token)
    {
        try { return Read<FormPublicationTicket>(TicketProtector(), Convert.FromBase64String(token)); }
        catch (CryptographicException) { return null; }
        catch (FormatException) { return null; }
        catch (JsonException) { return null; }
    }
    public byte[] ProtectManifest(FormPublicationProtectionContext context, FormPublicationManifest manifest) => Protect(ManifestProtector(context), manifest);
    public FormPublicationManifest ReadManifest(FormPublicationProtectionContext context, byte[] protectedManifest)
    {
        var manifest = Read<FormPublicationManifest>(ManifestProtector(context), protectedManifest);
        if (manifest.Format != 1 || manifest.DefinitionId != context.DefinitionId || manifest.VersionId != context.VersionId)
            throw new InvalidOperationException("The publication manifest does not match its owner.");
        return manifest;
    }
    private IDataProtector TicketProtector() => provider.CreateProtector("ArquitecturaBase.BusinessForms.PublicationChecks.v1");
    private IDataProtector ManifestProtector(FormPublicationProtectionContext context) => provider.CreateProtector(
        "ArquitecturaBase.BusinessForms.PublicationManifests.v1", context.OperationId.ToString("N"), context.Actor.ToString("N"),
        context.RequestId.ToString("N"), context.DefinitionId.ToString("N"), context.VersionId.ToString("N"));
    private static byte[] Protect<T>(IDataProtector protector, T value)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        try { return protector.Protect(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    private static T Read<T>(IDataProtector protector, byte[] ciphertext)
    {
        var plaintext = protector.Unprotect(ciphertext);
        try { return JsonSerializer.Deserialize<T>(plaintext, Options) ?? throw new InvalidOperationException("The protected publication data is invalid."); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}
