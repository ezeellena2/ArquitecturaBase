using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>
/// La clave fiscal de quien entra a ARCA, sólo en memoria (ADR 0026): no se imprime, no se serializa ni se guarda. El robot
/// la lee con <see cref="Reveal"/> para llenar el campo de ingreso y la suelta apenas ARCA acepta el ingreso.
/// </summary>
[JsonConverter(typeof(ArcaFiscalKeyJsonConverter))]
public sealed class ArcaFiscalKey
{
    private readonly string value;

    public ArcaFiscalKey(string? value) => this.value = value ?? string.Empty;

    public bool IsEmpty => string.IsNullOrWhiteSpace(value);

    public string Reveal() => value;

    public override string ToString() => "[REDACTED]";
}

/// <summary>Nadie serializa la clave: escribirla falla, para que un log o una respuesta nunca la lleve.</summary>
internal sealed class ArcaFiscalKeyJsonConverter : JsonConverter<ArcaFiscalKey>
{
    public override ArcaFiscalKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("The tax key is never deserialized; the HTTP contract hands it over.");

    public override void Write(Utf8JsonWriter writer, ArcaFiscalKey value, JsonSerializerOptions options) =>
        throw new NotSupportedException("The tax key is never serialized.");
}

/// <summary>Con qué se entra a ARCA del lado del cliente: la CUIT o CUIL de quien entra y su clave.</summary>
public sealed record ArcaFiscalCredentials(string LoginCuit, ArcaFiscalKey Key)
{
    public override string ToString() => $"ArcaFiscalCredentials {{ login: {CuitNumber.Mask(LoginCuit)}, key: [REDACTED] }}";
}

/// <summary>El pedido de arrancar el lado del cliente: el consentimiento explícito, el ingreso y la revisión que vio la pantalla.</summary>
public sealed record ArcaRobotStartRequest(bool Consent, string LoginCuit, ArcaFiscalKey FiscalKey, long ExpectedRevision)
{
    public override string ToString() =>
        $"ArcaRobotStartRequest {{ consent: {Consent}, login: {CuitNumber.Mask(CuitNumber.Normalize(LoginCuit) ?? string.Empty)}, key: [REDACTED] }}";
}
