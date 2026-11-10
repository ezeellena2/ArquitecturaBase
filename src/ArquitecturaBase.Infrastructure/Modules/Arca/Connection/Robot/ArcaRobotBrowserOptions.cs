using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;

/// <summary>
/// Lo que necesita el navegador del robot (<c>Arca:Robot</c>, ADR 0026): la página de ingreso de ARCA, los nombres de los
/// servicios tal como los muestra, los plazos y la clave fiscal de la plataforma, que es un secreto de la instalación
/// (user-secrets, Key Vault o variables protegidas) y nunca va a la base. Sólo se valida con el robot prendido.
/// </summary>
internal sealed class ArcaRobotBrowserOptions
{
    public bool Enabled { get; init; }

    /// <summary>La página de ingreso con clave fiscal; las pruebas la apuntan a las páginas simuladas.</summary>
    public Uri LoginUrl { get; init; } = new("https://auth.afip.gob.ar/contribuyente_/login.xhtml");

    public bool Headless { get; init; } = true;

    /// <summary>Lo que espera cada acción a que aparezca lo esperado antes de decir que la pantalla cambió.</summary>
    public TimeSpan StepTimeout { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>El plazo de una pasada entera; vencido, el robot se frena como si ARCA no respondiera.</summary>
    public TimeSpan RunTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Cada cuánto mira el worker si hay un lado de la plataforma vencido.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Las corridas del cliente que un host acepta en espera; más allá, la pantalla recibe «el robot no puede ahora».</summary>
    public int QueueCapacity { get; init; } = 4;

    /// <summary>Las pruebas lo apagan para correr el worker a mano, con el reloj del factory.</summary>
    public bool ProcessInBackground { get; init; } = true;

    /// <summary>El nombre de cada servicio en el Administrador de Relaciones, por código WSN; se corrige sin tocar código.</summary>
    public Dictionary<string, string> ServiceLabels { get; init; } = new(StringComparer.Ordinal)
    {
        ["wsfe"] = "Facturación Electrónica",
        ["ws_sr_constancia_inscripcion"] = "Consulta de Constancia de Inscripción",
    };

    public ArcaRobotPlatformCredentials Platform { get; init; } = new();

    public bool IsValid() => !Enabled || (LoginUrl.Scheme == Uri.UriSchemeHttps || LoginUrl.IsLoopback)
        && StepTimeout > TimeSpan.Zero && RunTimeout > StepTimeout && PollInterval > TimeSpan.Zero && QueueCapacity is > 0 and <= 50
        && Platform.IsValid();
}

/// <summary>Con qué entra el robot del lado de la plataforma: la CUIT o CUIL de ingreso y su clave fiscal.</summary>
internal sealed class ArcaRobotPlatformCredentials
{
    public string? LoginCuit { get; init; }

    public string? FiscalKey { get; init; }

    public bool IsValid() => CuitNumber.Normalize(LoginCuit ?? string.Empty) is not null && !string.IsNullOrWhiteSpace(FiscalKey);

    public override string ToString() => "ArcaRobotPlatformCredentials { key: [REDACTED] }";
}
