using ArquitecturaBase.Application.Modules.Arca.Models.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;

/// <summary>
/// Pasa el lado del cliente al worker de este mismo host, en memoria (ADR 0026): la clave nunca va a la base, a la cola
/// cifrada ni a la red interna. Se llama después del commit que reserva la corrida.
/// </summary>
public interface IArcaCompanyRobotQueue
{
    /// <summary>False si el host no puede tomar otra corrida ahora; la corrida queda fallida y la clave se suelta.</summary>
    bool TryEnqueue(ArcaCompanyRobotWorkResponse job, ArcaFiscalCredentials credentials);
}
