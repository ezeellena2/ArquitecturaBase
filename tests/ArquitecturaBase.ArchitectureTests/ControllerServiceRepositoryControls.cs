using ArquitecturaBase.Application.Modules.Control.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.Control.Interfaces.Services;

// Casos de control de ControllerServiceRepositoryTests, en un módulo inventado: un controller que inyecta el servicio de
// su módulo, como uno del núcleo, y otro que además inyecta un repositorio del módulo. Viven solo en este ensamblado de
// tests y no se ejecutan nunca: solo importan sus tipos.
namespace ArquitecturaBase.Application.Modules.Control.Interfaces.Services
{
    public interface IControlService;
}

namespace ArquitecturaBase.Application.Modules.Control.Interfaces.Persistence
{
    public interface IControlRepository;
}

namespace ArquitecturaBase.Api.Modules.Control.Controllers
{
    public sealed class ControlServiceController(IControlService service)
    {
        public IControlService Service { get; } = service;
    }

    public sealed class ControlRepositoryController(IControlService service, IControlRepository repository)
    {
        public IControlService Service { get; } = service;

        public IControlRepository Repository { get; } = repository;
    }
}
