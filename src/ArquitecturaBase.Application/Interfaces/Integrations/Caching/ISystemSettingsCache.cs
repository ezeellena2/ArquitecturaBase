namespace ArquitecturaBase.Application.Interfaces.Integrations.Caching;

/// <summary>
/// El caché de los ajustes que lee <see cref="Persistence.ISystemSettingsReader"/>. Está aparte del lector porque
/// descartarlo no es leer: lo llama el servicio que guarda los ajustes, después del commit y solo si se confirmó.
/// </summary>
public interface ISystemSettingsCache
{
    /// <summary>Descarta lo cacheado: lo que se cambia desde el panel vale al instante, sin reiniciar nada.</summary>
    Task InvalidateAsync(CancellationToken cancellationToken);
}
