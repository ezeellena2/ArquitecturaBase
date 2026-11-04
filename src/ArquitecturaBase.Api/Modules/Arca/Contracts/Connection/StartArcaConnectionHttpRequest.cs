using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Api.Modules.Arca.Contracts.Connection;

/// <summary>El alta o la renovación: con una conexión que ya existe, cuentan sólo el alias y la revisión.</summary>
public sealed record StartArcaConnectionHttpRequest(string Cuit, ArcaEnvironment Environment, string Alias, IReadOnlyList<string>? Services,
    long ExpectedRevision)
{
    public override string ToString() =>
        $"StartArcaConnectionHttpRequest {{ cuit: {CuitNumber.Mask(CuitNumber.Normalize(Cuit) ?? string.Empty)}, environment: {Environment} }}";
}
