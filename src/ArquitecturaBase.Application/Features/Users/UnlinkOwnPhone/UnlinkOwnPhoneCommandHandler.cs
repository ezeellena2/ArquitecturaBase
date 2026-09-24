using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users.UnlinkOwnPhone;

/// <summary>
/// La persona desvincula su propio WhatsApp (sección 12 del spec del ingreso con WhatsApp), solo si le queda otro medio
/// de ingreso (<see cref="UserGuards.HasOtherLoginMethodAsync"/>). Lo que promete la confirmación del perfil es que ya
/// no va a poder entrar con ese número ni desde el chat: por eso, además de sacarle el número, se suelta su contacto de
/// WhatsApp (si no, el bot la seguiría encontrando por él) y se invalidan los enlaces de ingreso que el bot ya mandó al
/// chat y todavía sirven (<see cref="PhoneNumberChange"/>). Las sesiones no se cierran: lo hace la misma persona, que
/// sigue adentro. Cerrarlas es del desvincular de un administrador. Una cuenta sin número responde lo mismo, y aun así
/// suelta el contacto y los enlaces que le hayan quedado: es la forma que tiene la persona de cortar un chat que siguiera
/// apuntando a su cuenta.
/// </summary>
internal sealed class UnlinkOwnPhoneCommandHandler(
    ICurrentUser currentUser,
    IIdentityService identityService,
    UserGuards guards,
    WhatsAppContactLinker contactLinker,
    PhoneNumberChange phoneChange)
    : ICommandHandler<UnlinkOwnPhoneCommand>
{
    public async Task<Result> Handle(UnlinkOwnPhoneCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return UserErrors.NotFound;
        }

        // Los locks van antes de todo, primero el contacto y después la cuenta, como el bot. Si el bot le está contestando
        // a este chat, se lo espera, y el enlace que mande se invalida acá.
        await phoneChange.LockAsync(userId, newPhone: null, cancellationToken);

        // La cuenta se lee recién ahora, con los locks: mientras se esperaba al bot, el bot pudo haberla escrito (verifica
        // el número del chat si estaba sin verificar), y con lo leído antes el guardado chocaría con su ConcurrencyStamp.
        var user = await identityService.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        // Sin número no hay medio de ingreso que perder: solo queda soltar lo que haya quedado de uno anterior.
        if (user.PhoneNumber is not null)
        {
            if (!await guards.HasOtherLoginMethodAsync(user, cancellationToken))
            {
                return UserErrors.LastLoginMethod;
            }

            await identityService.RemovePhoneAsync(user.Id, cancellationToken);
        }

        await contactLinker.UnlinkUserAsync(user.Id, cancellationToken);
        await phoneChange.VoidPendingLinksAsync(user.Id, cancellationToken);

        return Result.Success();
    }
}
