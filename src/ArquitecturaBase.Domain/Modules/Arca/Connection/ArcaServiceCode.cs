namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

/// <summary>
/// El código de un servicio de ARCA en el WSAA (WSN): "wsfe", "ws_sr_constancia_inscripcion", "wscpe". Lo declara cada
/// familia; la conexión no tiene una lista cerrada (ADR 0025).
/// </summary>
public readonly record struct ArcaServiceCode
{
    private ArcaServiceCode(string value) => Value = value;

    public string Value { get; }

    public static ArcaServiceCode? Create(string? value) =>
        value is { Length: >= 2 and <= 40 } && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')
            ? new ArcaServiceCode(value)
            : null;

    public override string ToString() => Value;
}
