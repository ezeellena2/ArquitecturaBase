namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

/// <summary>
/// Cómo entra la instalación a ARCA: con el certificado de la empresa (ADR 0023), en delegación guiada (ADR 0027) o en
/// delegación con robot (ADR 0026), que se ofrece sólo con el robot prendido.
/// </summary>
public enum ArcaAccessMode
{
    CompanyCertificate,
    PlatformDelegation,
    PlatformDelegationAutomated,
}
