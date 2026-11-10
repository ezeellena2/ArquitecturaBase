using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;
// `form-channels`: a form channel is installed, so the editor offers «Dónde se completa» (SA1-A). The channels are scoped and
// this source is a singleton read at startup, so it asks the container whether one is registered instead of resolving it.
internal sealed class FormClientCapabilitySource(IPrivateFileStore files, IServiceProviderIsService services) : IClientCapabilitySource
{
    public IReadOnlyCollection<string> InstalledFeatures => [ "forms", .. files.IsAvailable ? ["form-files"] : Array.Empty<string>(),
        .. services.IsService(typeof(IStructuredFormChannel)) ? ["form-channels"] : Array.Empty<string>() ];
}
