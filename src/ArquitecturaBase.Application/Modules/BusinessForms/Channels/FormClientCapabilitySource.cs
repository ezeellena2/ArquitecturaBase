using ArquitecturaBase.Application.Interfaces.Channels;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;
internal sealed class FormClientCapabilitySource : IClientCapabilitySource
{
    public IReadOnlyCollection<string> InstalledFeatures => ["forms"];
}
