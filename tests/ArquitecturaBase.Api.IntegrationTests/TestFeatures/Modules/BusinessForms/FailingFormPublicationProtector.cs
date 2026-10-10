using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
using Microsoft.AspNetCore.DataProtection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed class FailingFormPublicationProtector(IDataProtectionProvider provider) : IFormPublicationProtector
{
    private readonly DataProtectionFormPublicationProtector _inner = new(provider);
    public string ProtectTicket(FormPublicationTicket ticket) => _inner.ProtectTicket(ticket);
    public FormPublicationTicket? ReadTicket(string token) => _inner.ReadTicket(token);
    public byte[] ProtectManifest(FormPublicationProtectionContext context, FormPublicationManifest manifest) =>
        throw new InvalidOperationException("Expected publication manifest protection failure.");
    public FormPublicationManifest ReadManifest(FormPublicationProtectionContext context, byte[] protectedManifest) => _inner.ReadManifest(context, protectedManifest);
}
