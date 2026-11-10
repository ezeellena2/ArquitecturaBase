namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

/// <summary>
/// Las claves estables de los pasos del robot (ADR 0026). Las de delegar, aceptar y asignar son las de la guía de la etapa
/// 5, así la pantalla la abre donde se frenó el robot; las de cada familia (el punto de venta de Facturación) las declara
/// la familia.
/// </summary>
public static class ArcaRobotSteps
{
    public const string CompanyLogin = "company.login";
    public const string PlatformLogin = "platform.login";
    public const string PlatformCertificate = "platform.certificate";
    public const string PlatformAccept = "platform.accept";
    public const string Verify = "verify";

    public static string CompanyDelegate(ArcaServiceCode service) => "company.delegate." + service.Value;

    public static string PlatformAuthorize(ArcaServiceCode service) => "platform.authorize." + service.Value;

    public static string PlatformAssign(ArcaServiceCode service) => "platform.assign." + service.Value;
}
