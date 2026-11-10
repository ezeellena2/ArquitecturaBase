using ArquitecturaBase.Application.Modules.Arca.Configuration;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Connection;

/// <summary>
/// Qué modos de acceso ofrece la instalación: el certificado de la empresa siempre, la delegación guiada con la plataforma
/// configurada (ADR 0027) y la del robot, además, con el robot prendido (ADR 0026).
/// </summary>
internal sealed class ArcaAccessModePolicy(IOptions<ArcaPlatformOptions> platform, IOptions<ArcaRobotOptions> robot)
{
    /// <summary>La plataforma configurada en la instalación, o null.</summary>
    public ArcaPlatformResponse? Platform => platform.Value.ToResponse();

    public bool Offers(ArcaAccessMode mode) => mode switch
    {
        ArcaAccessMode.CompanyCertificate or ArcaAccessMode.PlatformDelegation => true,
        ArcaAccessMode.PlatformDelegationAutomated => robot.Value.Enabled,
        _ => false,
    };
}
