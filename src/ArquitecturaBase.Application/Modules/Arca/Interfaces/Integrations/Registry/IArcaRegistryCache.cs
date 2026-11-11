using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Registry;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Registry;

/// <summary>
/// La constancia de un tercero guardada diez minutos en Redis, por conexión y CUIT, para que la vista previa de «¿La emito?»,
/// que se vuelve a pedir con cada cambio, «Completar con ARCA» y la emisión que sigue no vuelvan a ARCA. Si no la tiene, la
/// consulta con <see cref="IArcaRegistryClient"/> y la sesión recibida; guarda sólo una respuesta con datos, así una falla se
/// vuelve a consultar la próxima vez.
/// </summary>
public interface IArcaRegistryCache
{
    Task<ArcaServiceReply<ArcaRegistryPerson>> FindPersonAsync(ArcaServiceSession session, string cuit, CancellationToken cancellationToken);
}
