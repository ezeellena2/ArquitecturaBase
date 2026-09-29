using ArquitecturaBase.Application.Interfaces.Channels;

// Casos de control de DependencyInjectionTests: un puerto del núcleo hacia un módulo, con un adaptador, y un contrato de
// servicio y un helper de un módulo inventado. Viven solo en este ensamblado de tests y no se ejecutan en src.
namespace ArquitecturaBase.Application.Interfaces.Channels
{
    internal interface IControlChannel;
}

namespace ArquitecturaBase.Application.Channels
{
    internal sealed class ControlChannel : IControlChannel;
}

namespace ArquitecturaBase.Application.Modules.Control.Interfaces.Services
{
    internal interface IControlService;
}

namespace ArquitecturaBase.Application.Modules.Control.Services
{
    internal sealed class ControlGuard;
}
