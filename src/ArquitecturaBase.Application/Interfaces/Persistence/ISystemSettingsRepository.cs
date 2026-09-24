using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>La única fila de ajustes. No hay listado ni borrado: la fila se crea una vez y después se edita.</summary>
public interface ISystemSettingsRepository
{
    /// <summary>La fila de ajustes, o null si el seed todavía no la creó.</summary>
    Task<SystemSettings?> GetAsync(CancellationToken cancellationToken);

    void Add(SystemSettings settings);
}
