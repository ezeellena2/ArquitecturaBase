using ArquitecturaBase.Application.Features.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Users;

/// <summary>
/// Lo que comparten los cambios del número de una cuenta (sección 12 del spec del ingreso con WhatsApp): vincularlo,
/// reemplazarlo por otro y desvincularlo. Lo usan el perfil y, después, el desvincular de un administrador. Toma los
/// locks en el orden del bot y, cuando la cuenta suelta un número, invalida los enlaces de ingreso que el bot ya mandó
/// a ese chat. Para el número que se va, reemplazarlo es lo mismo que desvincularlo: su chat puede no ser más de esta
/// persona (el caso del teléfono robado), y los enlaces van atados a la cuenta, no al número. Las sesiones no se tocan:
/// eso lo decide quien llama.
/// </summary>
internal sealed class PhoneNumberChange(
    WhatsAppContactLinker contactLinker,
    ILoginLinkRepository loginLinks,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Toma los locks antes de escribir nada de la cuenta, en el mismo orden que el bot: primero las filas de los
    /// contactos que se van a tocar (el de la cuenta y, si pasa a <paramref name="newPhone"/>, los de ese número) y
    /// después el de los enlaces de la cuenta, con el que un canje en curso termina antes de que se invaliden. El bot
    /// toma el contacto y después la cuenta: al revés, cada uno espera al otro, Postgres corta a uno con un deadlock
    /// (40P01) y, si le toca al perfil, sale un 500. En el mismo orden, el que llega segundo espera al primero. Las dos
    /// excepciones las resuelve el bot: el chat que contesta puede no estar entre estas filas (otro contacto del número,
    /// sin vincular), así que con el lock de la cuenta vuelve a mirar si el número sigue siendo de ella; y el contacto de
    /// la cuenta que suelta para vincular el suyo no lo espera (<see cref="WhatsAppContactLinker.LinkAsync"/>). Abre la
    /// transacción si no hay una, así el número, el contacto y los enlaces se guardan juntos. Quien llama lee la cuenta
    /// después, no antes: mientras espera, el bot puede escribirla (verifica el número del chat), y un guardado hecho con
    /// lo leído antes chocaría con el ConcurrencyStamp de Identity, que también es un 500.
    /// </summary>
    public async Task LockAsync(Guid userId, PhoneNumber? newPhone, CancellationToken cancellationToken)
    {
        await contactLinker.LockAsync(userId, newPhone, cancellationToken);
        await loginLinks.LockAccountAsync(userId, cancellationToken);
    }

    /// <summary>
    /// La cuenta soltó un número: invalida los enlaces de ingreso que todavía sirven, que el bot pudo haber mandado al
    /// chat de ese número. Va después de guardar el número: si el guardado fallara, la cuenta conserva el que tenía, y
    /// los enlaces de su chat siguen sirviendo.
    /// </summary>
    public async Task VoidPendingLinksAsync(Guid userId, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var link in await loginLinks.ListActiveAsync(userId, nowUtc, cancellationToken))
        {
            link.Invalidate(nowUtc);
        }
    }
}
