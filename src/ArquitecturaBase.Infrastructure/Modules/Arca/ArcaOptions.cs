namespace ArquitecturaBase.Infrastructure.Modules.Arca;

/// Las direcciones y los plazos de ARCA por ambiente. Son configuración porque los documentos de ARCA alternan entre
/// afip.gov.ar y arca.gov.ar; los valores por defecto están en la sección "Arca" de appsettings.json.
public sealed record ArcaOptions
{
    public ArcaEnvironmentOptions Testing { get; init; } = new();
    public ArcaEnvironmentOptions Production { get; init; } = new();
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public int MaxResponseBytes { get; init; } = 512 * 1024;
    public TimeSpan TicketMargin { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan TicketLease { get; init; } = TimeSpan.FromSeconds(60);

    public bool IsValid() => Testing.IsValid() && Production.IsValid() && RequestTimeout > TimeSpan.Zero && MaxResponseBytes > 0
        && TicketMargin > TimeSpan.Zero && TicketLease > TimeSpan.Zero;
}

public sealed record ArcaEnvironmentOptions
{
    public Uri? Wsaa { get; init; }

    /// La dirección de cada servicio por su código WSN ("wsfe", "ws_sr_constancia_inscripcion"); cada familia suma los suyos.
    public Dictionary<string, Uri> Services { get; init; } = new(StringComparer.Ordinal);

    /// El CN del emisor de los certificados de este ambiente: dice si un .crt es de homologación o de producción.
    public string IssuerCommonName { get; init; } = string.Empty;

    /// Lo que ARCA tarda en aceptar otro pedido de ticket para el mismo servicio (10 minutos en homologación, 2 en producción).
    public TimeSpan TicketRetryWindow { get; init; }

    public bool IsValid() => IsHttps(Wsaa) && Services.Values.All(IsHttps)
        && !string.IsNullOrWhiteSpace(IssuerCommonName) && TicketRetryWindow > TimeSpan.Zero;

    private static bool IsHttps(Uri? address) => address is { IsAbsoluteUri: true, Scheme: "https" };
}
