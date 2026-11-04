namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

/// <summary>Cómo entra la instalación a ARCA. La etapa 1 habilita solo el certificado de la empresa (ADR 0023).</summary>
public enum ArcaAccessMode
{
    CompanyCertificate,
    PlatformDelegation,
    PlatformDelegationAutomated,
}
