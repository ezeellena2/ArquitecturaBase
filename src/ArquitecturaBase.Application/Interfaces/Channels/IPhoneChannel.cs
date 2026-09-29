using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// El canal por el que se mandan códigos a un número de teléfono. Lo leen los medios de ingreso (GET
/// /account/login-methods) y el alta y la edición de una cuenta, que controlan el país de un número nuevo. Hay uno solo:
/// sin módulo, DisabledPhoneChannel (apagado, sin países, acepta cualquier país); con un módulo que lo da, el del módulo,
/// que reemplaza al apagado.
/// </summary>
public interface IPhoneChannel
{
    /// <summary>Si se pueden mandar códigos por teléfono. Apagado, el ingreso no ofrece la opción.</summary>
    bool IsEnabled { get; }

    /// <summary>Los países a los que se mandan códigos, ISO 3166-1 alfa-2 en mayúsculas. Vacío si está apagado.</summary>
    IReadOnlyList<string> Countries { get; }

    /// <summary>El número del canal, solo dígitos, para volver a él desde la web; null si no hay.</summary>
    string? DisplayNumber { get; }

    /// <summary>
    /// Si se le pueden mandar códigos a <paramref name="phone"/> por su país. Vale aunque el canal esté apagado: es la regla
    /// de un número nuevo en el alta y la edición. El error es el del canal.
    /// </summary>
    Result EnsureCanSendTo(PhoneNumber phone);
}
