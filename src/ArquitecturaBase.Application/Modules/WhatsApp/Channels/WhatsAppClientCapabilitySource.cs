using ArquitecturaBase.Application.Interfaces.Channels;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Channels;

// `whatsapp`: the module is installed, so Administración › Configuración shows «WhatsApp» (RC3-A) to whoever manages the settings, with
// the transport on or off: the retention of the messages runs anyway.
internal sealed class WhatsAppClientCapabilitySource : IClientCapabilitySource
{
    public IReadOnlyCollection<string> InstalledFeatures => ["whatsapp"];
}
