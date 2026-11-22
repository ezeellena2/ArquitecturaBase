namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection;

/// <summary>
/// RC7-B: el worker del aviso de vencimiento del certificado (<c>Arca:CertificateNotice</c>). Mira al arrancar y después cada
/// <see cref="Interval"/>; los días del aviso son de la conexión, en la pantalla, no de acá. Apagado, lo corren los tests.
/// </summary>
internal sealed class ArcaCertificateNoticeOptions
{
    public bool RunInBackground { get; init; } = true;
    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    public bool IsValid() => Interval >= TimeSpan.FromMinutes(1) && Interval <= TimeSpan.FromDays(1);
}
