using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Api.Modules.Arca.Contracts.Connection;

/// <summary>
/// El lado del cliente del robot (ADR 0026, RB1-A): el consentimiento explícito, la CUIT o CUIL con la que se entra a ARCA,
/// la clave fiscal y la revisión que vio la pantalla. La clave pasa enseguida a un tipo que no se imprime ni se serializa.
/// </summary>
public sealed record StartArcaRobotRunHttpRequest(bool Consent, string? LoginCuit, string? FiscalKey, long ExpectedRevision)
{
    public override string ToString() =>
        $"StartArcaRobotRunHttpRequest {{ consent: {Consent}, login: {CuitNumber.Mask(CuitNumber.Normalize(LoginCuit ?? string.Empty) ?? string.Empty)}, fiscalKey: [REDACTED] }}";
}
