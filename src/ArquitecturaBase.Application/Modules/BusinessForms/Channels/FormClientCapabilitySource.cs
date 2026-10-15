using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;
internal sealed class FormClientCapabilitySource(IPrivateFileStore files) : IClientCapabilitySource
{
    public IReadOnlyCollection<string> InstalledFeatures => files.IsAvailable ? ["forms", "form-files"] : ["forms"];
}
