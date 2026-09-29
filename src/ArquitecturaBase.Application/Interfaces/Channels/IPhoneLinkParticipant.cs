using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// Algo que un módulo ata al número de una cuenta (con WhatsApp, el contacto del chat). PhoneNumberLinker lo avisa en
/// cada cambio del número: vincularlo desde el perfil, reemplazarlo o quitarlo desde el perfil o la administración. Puede
/// haber cero o más; se llaman en el orden en que se registraron. Todos los métodos corren adentro del límite del caso de
/// uso: no abren ni confirman transacciones, y sus locks la exigen.
/// </summary>
public interface IPhoneLinkParticipant
{
    /// <summary>
    /// Toma los locks de lo que va a tocar el cambio: lo atado a la cuenta y, si pasa a <paramref name="newPhone"/>, lo
    /// atado a ese número. PhoneNumberLinker lo llama ANTES del lock de la cuenta (login-link:), que es el orden del bot:
    /// al revés, el perfil y el bot se esperan mutuamente y Postgres corta a uno con un deadlock (40P01). Espera a quien
    /// tenga esos locks. Quien llama lee la cuenta después.
    /// </summary>
    Task LockAsync(Guid userId, PhoneNumber? newPhone, CancellationToken cancellationToken);

    /// <summary>
    /// La cuenta probó desde el perfil que <paramref name="phone"/> es suyo, y ya quedó guardado. Puede reemplazar a otro
    /// número: ese reemplazo llega solo por acá, sin <see cref="PhoneReleasedAsync"/>, así que el módulo ata lo suyo a
    /// <paramref name="phone"/> y suelta lo que tuviera atado la cuenta.
    /// </summary>
    Task PhoneConfirmedAsync(Guid userId, PhoneNumber phone, CancellationToken cancellationToken);

    /// <summary>
    /// La cuenta soltó su número: lo quitó (desde el perfil o la administración) o un administrador lo reemplazó por uno
    /// sin confirmar. El módulo suelta lo que tenía atado.
    /// </summary>
    Task PhoneReleasedAsync(Guid userId, CancellationToken cancellationToken);
}
