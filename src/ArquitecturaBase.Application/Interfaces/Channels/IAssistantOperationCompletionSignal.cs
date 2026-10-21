namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// Avisa después de confirmar un resultado terminal del motor. El identificador capturado es opaco para el motor;
/// sólo el dueño del canal lo interpreta. Es una optimización en memoria: no reemplaza la recuperación periódica.
/// </summary>
public interface IAssistantOperationCompletionSignal
{
    /// <summary>Nunca espera ni transporta contenido de la operación.</summary>
    void Notify(string channelId);
}
