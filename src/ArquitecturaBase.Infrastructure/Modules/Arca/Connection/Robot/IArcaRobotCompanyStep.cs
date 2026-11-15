using ArquitecturaBase.Application.Modules.Arca.Models.Connection;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;

/// <summary>
/// Un paso del lado del cliente que aporta una familia (ADR 0026, decisión 7), como aporta su prueba con
/// <c>IArcaServiceDefinition</c>: Facturación da de alta el punto de venta. <c>Connection</c> no la nombra; cada familia lo
/// registra con <c>TryAddEnumerable</c>. Antes de actuar mira si ya está hecho.
/// </summary>
internal interface IArcaRobotCompanyStep
{
    /// <summary>La clave estable del paso, la misma que usa la guía de la etapa 5 (por ejemplo <c>company.point-of-sale</c>).</summary>
    string Key { get; }

    bool AppliesTo(IReadOnlyCollection<string> enabledServices);

    /// <summary>
    /// Corre el paso en la sesión del cliente; devuelve lo que la pantalla tiene que mostrar (el número), sin secretos. Con
    /// <paramref name="record"/> guarda en la corrida lo que va a dar antes de confirmarlo en ARCA (O10); false sin la reserva.
    /// </summary>
    Task<string?> RunAsync(ArcaRobotSession session, ArcaCompanyRobotWorkResponse job, Func<string, Task<bool>> record,
        CancellationToken cancellationToken);
}
