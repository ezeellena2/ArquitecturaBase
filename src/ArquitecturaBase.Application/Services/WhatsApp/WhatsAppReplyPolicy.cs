using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;

namespace ArquitecturaBase.Application.Services.WhatsApp;

/// <summary>
/// La tabla de la sección 8 del spec del ingreso con WhatsApp: busca la cuenta del chat, con su lock, y decide la
/// respuesta. Lo que escribe para una cuenta (el enlace, el alta) lo hace <see cref="WhatsAppLinkIssuer"/>. La usa
/// <see cref="WhatsAppInboundService"/>, que ya tiene el contacto con su lock y abrió el límite: acá no se abre ni se
/// confirma nada. Recibe <see cref="ISignInService"/> solo para mirar el bloqueo (<c>IdentityBoundaryTests</c>).
/// </summary>
internal sealed class WhatsAppReplyPolicy(
    IUserReader users,
    ISignInService signIn,
    AccountCreationPolicy accountCreation,
    ILoginLinkRepository accountLocks,
    WhatsAppLinkIssuer links,
    IPublicOrigin publicOrigin,
    IAppName appName)
{
    /// <summary>La pantalla de ingreso del SPA, a la que lleva el botón "Ir a la web".</summary>
    private const string WebLoginPath = "login";

    /// <summary>La tabla de la sección 8 del spec, en orden: primero la cuenta del número y después el botón.</summary>
    public async Task<WhatsAppOutboundMessage> DecideAsync(
        WhatsAppContact contact,
        PhoneNumber phone,
        string? replyId,
        CancellationToken cancellationToken)
    {
        var account = await FindAccountAsync(contact, phone, cancellationToken);

        if (account is not null)
        {
            // Una cuenta activa recibe el enlace sea cual sea el mensaje, incluidos «Crear cuenta» tocado dos veces y
            // «Quiero entrar» de la invitación.
            return account.IsActive && !await signIn.IsLockedOutAsync(account.Id, cancellationToken)
                ? await links.SendLoginLinkAsync(contact, phone, account, cancellationToken)
                : Reply(account).Disabled(phone);
        }

        // Una cuenta borrada conserva su número (sección 6.1 del spec) y ninguna de las búsquedas de arriba la encuentra.
        // El bot la trata como deshabilitada, también en el idioma: le habla en el de la cuenta, no en el de "sin cuenta".
        if (await users.FindDeletedByPhoneAsync(phone, cancellationToken) is { } deleted)
        {
            return Reply(deleted).Disabled(phone);
        }

        // Sin cuenta, el bot habla en español.
        var reply = Reply(account: null);

        // Quién puede crear una cuenta lo decide la misma regla que el ingreso por código: con el número, solo Open.
        var registrationOpen = await accountCreation.AllowsNewAccountAsync(email: null, cancellationToken);

        return replyId switch
        {
            BotButtons.CreateAccount when registrationOpen => await links.CreateAccountAsync(contact, phone, cancellationToken),
            BotButtons.HaveAccount => reply.HaveAccount(phone, WebLoginUrl()),
            _ when registrationOpen => reply.NoAccount(phone),
            _ => reply.NotInvited(phone, WebLoginUrl()),
        };
    }

    /// <summary>
    /// Primero la cuenta del contacto vinculado y después la del número (sección 8 del spec). La del número no incluye
    /// las borradas: esas se reconocen aparte. La cuenta vuelve con su lock tomado, el de sus enlaces, que es el mismo
    /// que toma el perfil para cambiarle el número (<see cref="ArquitecturaBase.Application.Services.Users.PhoneNumberLinker"/>): así, lo que el bot decida
    /// para esta cuenta no se cruza con un cambio de su número a medio hacer. Por los dos caminos, la cuenta que vuelve
    /// es la que se leyó después de tomar el lock, nunca la de antes.
    /// </summary>
    private async Task<UserAccount?> FindAccountAsync(WhatsAppContact contact, PhoneNumber phone, CancellationToken cancellationToken)
    {
        if (contact.UserId is { } linkedUserId
            && await users.FindByIdAsync(linkedUserId, cancellationToken) is { } linked)
        {
            // Para cambiarle el número a la cuenta, el perfil toma antes la fila de su contacto, que es este y lo tiene el
            // bot: espera a que el bot termine, así que la cuenta sigue siendo la de este chat.
            await accountLocks.LockAccountAsync(linked.Id, cancellationToken);

            // La administración, en cambio, no toma el contacto: desactivar o borrar la cuenta toma solo este lock, así
            // que mientras el bot lo esperaba pudo cortarle el acceso. Se vuelve a leer (la consulta va a la base y ve
            // lo que la administración ya confirmó): con la lectura de antes, a una cuenta desactivada le mandaría un
            // enlace después del corte. Si la borraron, sigue por el número, que la reconoce como borrada.
            if (await users.FindByIdAsync(linked.Id, cancellationToken) is { } current)
            {
                return current;
            }
        }

        if (await users.FindByPhoneAsync(phone, cancellationToken) is not { } byNumber)
        {
            return null;
        }

        // Este contacto no es de la cuenta, y el perfil no espera por él: mientras el bot esperaba el lock, la persona pudo
        // haberle sacado el número o haberlo cambiado por otro. Con el lock, se vuelve a buscar (la consulta va a la base y
        // ve lo que el perfil ya confirmó). Si el número ya no es de la cuenta, el chat tampoco: sin esto, recibiría un
        // enlace de ella y quedaría vinculado a ella, y con él cada mensaje nuevo desde ese número traería otro enlace. Se
        // le contesta como a un número sin cuenta, aunque ya lo tenga otra: el próximo mensaje la encuentra.
        await accountLocks.LockAccountAsync(byNumber.Id, cancellationToken);

        // Se decide con la relectura y no con byNumber: mientras el bot esperaba, la administración pudo desactivar la
        // cuenta (toma solo este lock, como en el camino del contacto vinculado), y con la lectura de antes le mandaría
        // un enlace después del corte. Si la borró, el número ya no la encuentra y la reconoce FindDeletedByPhoneAsync.
        return await users.FindByPhoneAsync(phone, cancellationToken) is { } reread && reread.Id == byNumber.Id
            ? reread
            : null;
    }

    private BotReply Reply(UserAccount? account) => BotReply.For(appName.Value, account);

    private string WebLoginUrl()
    {
        var origin = publicOrigin.Value
            ?? throw new InvalidOperationException(
                "Authentication:Issuer must be set to the public origin of the web app to answer on WhatsApp.");

        return new Uri(origin, WebLoginPath).AbsoluteUri;
    }
}
