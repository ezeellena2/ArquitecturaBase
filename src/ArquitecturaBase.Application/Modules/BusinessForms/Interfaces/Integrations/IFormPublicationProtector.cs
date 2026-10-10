using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;

public interface IFormPublicationProtector
{
    string ProtectTicket(FormPublicationTicket ticket);
    FormPublicationTicket? ReadTicket(string token);
    byte[] ProtectManifest(FormPublicationProtectionContext context, FormPublicationManifest manifest);
    FormPublicationManifest ReadManifest(FormPublicationProtectionContext context, byte[] protectedManifest);
}
