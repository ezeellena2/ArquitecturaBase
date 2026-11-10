namespace ArquitecturaBase.Application.Modules.Arca.Configuration;

/// <summary>
/// El robot de delegación (<c>Arca:Robot</c>, ADR 0026). Apagado por defecto: se prende en cada instalación donde se ofrece
/// el modo 3. Lo que necesita el navegador (las páginas de ARCA, los plazos y la clave de la plataforma) lo lee
/// Infrastructure de la misma sección.
/// </summary>
public sealed class ArcaRobotOptions
{
    public bool Enabled { get; init; }

    /// <summary>A quién le llega el aviso cuando el robot se rompe; vacío, sólo queda el log (RB4-A).</summary>
    public string? AlertEmail { get; init; }

    /// <summary>Como mucho un aviso por motivo y paso dentro de este lapso.</summary>
    public TimeSpan AlertInterval { get; init; } = TimeSpan.FromDays(1);

    /// <summary>La reserva de un lado mientras corre; cada paso la renueva. Tiene que durar más que el paso más largo.</summary>
    public TimeSpan Lease { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>La espera del lado de la plataforma antes de volver a probar cuando ARCA no responde; crece con cada intento.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Los intentos del lado de la plataforma antes de darlo por fallido.</summary>
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Cuánto espera el lado de la plataforma al del cliente antes de volver a mirar; el cliente lo despierta al terminar.</summary>
    public TimeSpan CompanyWait { get; init; } = TimeSpan.FromHours(1);

    public bool IsValid() =>
        AlertInterval > TimeSpan.Zero && Lease >= TimeSpan.FromSeconds(30) && RetryDelay > TimeSpan.Zero && MaxAttempts is > 0 and <= 20
        && CompanyWait > TimeSpan.Zero && (string.IsNullOrWhiteSpace(AlertEmail) || AlertEmail.Contains('@', StringComparison.Ordinal));
}
