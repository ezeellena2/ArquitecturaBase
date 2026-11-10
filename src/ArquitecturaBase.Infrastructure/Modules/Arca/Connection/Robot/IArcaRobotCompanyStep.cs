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

    /// <summary>Corre el paso en la sesión del cliente; devuelve lo que la pantalla tiene que mostrar (el número), sin secretos.</summary>
    Task<string?> RunAsync(ArcaRobotSession session, ArcaCompanyRobotWorkResponse job, CancellationToken cancellationToken);
}
