using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Api.Modules.Arca.Contracts.Connection;

/// <summary>
/// El alta o la renovación: con una conexión que ya existe, cuentan sólo el alias y la revisión. Sin modo, es con el
/// certificado de la empresa; en delegación, el pedido es de la CUIT de la plataforma (ADR 0027).
/// </summary>
public sealed record StartArcaConnectionHttpRequest(string Cuit, ArcaEnvironment Environment, string Alias, IReadOnlyList<string>? Services,
    long ExpectedRevision, ArcaAccessMode? Mode = null)
{
    public override string ToString() =>
        $"StartArcaConnectionHttpRequest {{ cuit: {CuitNumber.Mask(CuitNumber.Normalize(Cuit) ?? string.Empty)}, environment: {Environment} }}";
}
