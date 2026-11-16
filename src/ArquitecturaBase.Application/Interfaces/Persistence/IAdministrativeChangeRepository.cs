using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// La historia de los cambios administrativos (N13). Sólo agrega filas: la guarda el límite del caso de uso que hizo el
/// cambio, así una y otro quedan o se deshacen juntos. No tiene lector: verla sería una pantalla nueva.
/// </summary>
public interface IAdministrativeChangeRepository
{
    void Add(AdministrativeChange change);
}
