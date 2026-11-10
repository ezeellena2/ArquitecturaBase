using ArquitecturaBase.Application.Modules.Arca.Models.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;

/// <summary>
/// El robot que maneja la página de ARCA (ADR 0026), con un navegador por pasada. Cada paso mira antes si ya está hecho, así
/// repetir una pasada no duplica nada en ARCA. Nunca lanza por lo que muestra ARCA: lo devuelve como un motivo estable. Los
/// pasos de <see cref="ArcaPlatformRobotWorkResponse.CompletedSteps"/> no se repiten.
/// </summary>
public interface IArcaDelegationRobot
{
    /// <summary>
    /// Con la clave del cliente, que sólo vive en esta llamada: entra, delega cada servicio en la CUIT de la plataforma y
    /// corre los pasos que aporta cada familia elegida (el punto de venta de Facturación).
    /// </summary>
    Task<ArcaRobotOutcome> RunCompanyAsync(ArcaCompanyRobotWorkResponse job, ArcaFiscalCredentials credentials, IArcaRobotProgress progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Con la clave de la plataforma, que es configuración: crea el certificado de la instalación si hace falta, autoriza las
    /// consultas para su alias y, si el cliente ya delegó, acepta y asigna cada servicio al alias.
    /// </summary>
    Task<ArcaRobotOutcome> RunPlatformAsync(ArcaPlatformRobotWorkResponse job, IArcaRobotProgress progress, CancellationToken cancellationToken);
}

/// <summary>
/// Lo que el robot avisa mientras trabaja. Cada aviso guarda en su transacción y renueva la reserva; false quiere decir que
/// la reserva ya no es propia (o que el certificado no pasó la revisión) y el robot se detiene sin seguir.
/// </summary>
public interface IArcaRobotProgress
{
    Task<bool> BeginAsync(string step, CancellationToken cancellationToken);

    Task<bool> CompleteAsync(string step, string? output, CancellationToken cancellationToken);

    /// <summary>Carga el certificado que bajó el robot por el mismo camino que la persona, con sus validaciones (RB5-A).</summary>
    Task<bool> InstallCertificateAsync(string certificatePem, CancellationToken cancellationToken);
}
