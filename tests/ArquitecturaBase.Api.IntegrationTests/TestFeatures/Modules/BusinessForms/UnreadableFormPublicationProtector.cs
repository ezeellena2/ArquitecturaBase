using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed class UnreadableFormPublicationProtector(IFormPublicationProtector inner) : IFormPublicationProtector
{
    public Guid? FailReadOperationId { get; set; }

    public string ProtectTicket(FormPublicationTicket ticket) => inner.ProtectTicket(ticket);
    public FormPublicationTicket? ReadTicket(string token) => inner.ReadTicket(token);
    public byte[] ProtectManifest(FormPublicationProtectionContext context, FormPublicationManifest manifest) => inner.ProtectManifest(context, manifest);

    public FormPublicationManifest ReadManifest(FormPublicationProtectionContext context, byte[] protectedManifest)
    {
        if (context.OperationId == FailReadOperationId) throw new InvalidOperationException("The test manifest cannot be opened.");
        return inner.ReadManifest(context, protectedManifest);
    }
}
