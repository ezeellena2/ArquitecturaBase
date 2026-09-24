using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.UnlinkUserPhone;

/// <summary>
/// Un administrador desvincula el WhatsApp de una cuenta (sección 12 del spec del ingreso con WhatsApp). Es la acción del
/// teléfono robado, así que corta el acceso en el momento, como desactivar: le saca el número, suelta su contacto de
/// WhatsApp (si no, el bot la seguiría encontrando por él) y cierra las sesiones con
/// <see cref="IIdentityService.RevokeSessionsAsync"/>, que también invalida los enlaces que el bot ya mandó. Puede dejar a
/// alguien sin medio de ingreso, salvo a sí mismo (<see cref="UserGuards.EnsurePhoneCanBeUnlinkedAsync"/>); sobre su
/// propia cuenta cierra también sus sesiones, y vuelve a entrar con su correo. Una cuenta sin número responde lo mismo, y
/// suelta el chat y los enlaces que le hayan quedado, sin cerrar sesiones: no había número que cortar.
/// </summary>
internal sealed class UnlinkUserPhoneCommandHandler(
    IIdentityService identityService,
    UserGuards guards,
    WhatsAppContactLinker contactLinker,
    PhoneNumberChange phoneChange)
    : ICommandHandler<UnlinkUserPhoneCommand>
{
    public async Task<Result> Handle(UnlinkUserPhoneCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Los locks van antes de todo, primero el contacto y después la cuenta, como el bot. Si el bot le está contestando
        // a este chat, se lo espera, y el enlace que mande se invalida acá.
        await phoneChange.LockAsync(command.UserId, newPhone: null, cancellationToken);

        // La cuenta se lee recién ahora, con los locks: mientras se esperaba al bot, el bot pudo haberla escrito (verifica
        // el número del chat si estaba sin verificar), y con lo leído antes el guardado chocaría con su ConcurrencyStamp.
        var user = await identityService.FindByIdAsync(command.UserId, cancellationToken);

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        if (user.PhoneNumber is null)
        {
            await contactLinker.UnlinkUserAsync(user.Id, cancellationToken);
            await phoneChange.VoidPendingLinksAsync(user.Id, cancellationToken);

            return Result.Success();
        }

        var allowed = await guards.EnsurePhoneCanBeUnlinkedAsync(user, cancellationToken);

        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        await identityService.RemovePhoneAsync(user.Id, cancellationToken);
        await contactLinker.UnlinkUserAsync(user.Id, cancellationToken);

        // Marcar la fila no impide nada: el access token vale 15 minutos y la cookie, 30 días (sección 7 del spec de la
        // Fase 4).
        await identityService.RevokeSessionsAsync(user.Id, cancellationToken);

        return Result.Success();
    }
}
