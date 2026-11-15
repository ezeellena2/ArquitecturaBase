using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Api.Modules.Arca.Contracts.Connection;

/// <summary>
/// Cambiar CUIT, ambiente o modo en el alta (H30): reemplaza la conexión leída, con su revisión, por una nueva con su pedido
/// de certificado, en un solo guardado. Sin modo, es con el certificado de la empresa.
/// </summary>
public sealed record ReplaceArcaConnectionHttpRequest(string Cuit, ArcaEnvironment Environment, string Alias, IReadOnlyList<string>? Services,
    long ExpectedRevision, ArcaAccessMode? Mode = null)
{
    public override string ToString() =>
        $"ReplaceArcaConnectionHttpRequest {{ cuit: {CuitNumber.Mask(CuitNumber.Normalize(Cuit) ?? string.Empty)}, environment: {Environment} }}";
}
