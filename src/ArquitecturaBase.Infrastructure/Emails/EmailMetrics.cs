using System.Diagnostics.Metrics;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// N5: en qué terminó cada intento del worker de correo, para los tableros, como WhatsApp cuenta los suyos (su <c>AddMeter</c>
/// está en <see cref="EmailRegistration"/>). Un contador por resultado: <c>sent</c>; <c>retry</c>, cuando vuelve a la cola a
/// esperar; <c>returned</c>, cuando la Api se apagó antes del cuerpo; <c>uncertain</c>; y los descartados, <c>rejected</c> (el
/// servidor lo rechazó para siempre, o un error) y <c>expired</c> (venció sin salir). Ninguna etiqueta lleva una dirección ni
/// un id. El medidor sale de <see cref="IMeterFactory"/>, uno por host: los tests escuchan sólo el del suyo.
/// </summary>
internal sealed class EmailMetrics
{
    public const string MeterName = "ArquitecturaBase.Email";
    public const string InstrumentName = "email.outcomes";

    public const string Sent = "sent";
    public const string Retry = "retry";
    public const string Returned = "returned";
    public const string Uncertain = "uncertain";
    public const string Rejected = "rejected";
    public const string Expired = "expired";

    private readonly Counter<long> _outcomes;

    public EmailMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        _outcomes = meterFactory.Create(MeterName).CreateCounter<long>(InstrumentName);
    }

    public void Record(string outcome) => _outcomes.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
