using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Integrations.WhatsApp;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.WhatsApp;

/// <summary>
/// El bot (sección 8 del spec del ingreso con WhatsApp). No tiene estado de conversación: decide con la cuenta del
/// número que escribe y con el botón que se tocó, y solo habla de esa cuenta. Toma el contacto con su lock, marca
/// procesados todos sus pendientes y les da una sola respuesta, en la misma transacción. Nunca abre una sesión ni emite
/// tokens (sección 5): lo máximo que produce es un enlace de un solo uso, mandado al mismo chat.
/// </summary>
internal sealed partial class WhatsAppInboundService(
    IWhatsAppContactRepository contacts,
    WhatsAppContactLinker contactLinker,
    IWhatsAppMessageRepository messages,
    IUserReader users,
    IUserRepository userRepository,
    ISignInService signIn,
    IPhoneNumberParser phoneNumbers,
    ILoginLinkRepository accountLocks,
    LoginLinkIssuer loginLinks,
    AccountCreationPolicy accountCreation,
    IPublicOrigin publicOrigin,
    IAppName appName,
    IWhatsAppOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<WhatsAppInboundService> logger)
    : IWhatsAppInboundService
{
    /// <summary>
    /// La ventana de atención de Meta: fuera de las 24 horas del mensaje de la persona, solo se le puede mandar una
    /// plantilla, y cualquier otra respuesta falla (131047).
    /// </summary>
    public static readonly TimeSpan ReplyWindow = TimeSpan.FromHours(24);

    /// <summary>La pantalla de ingreso del SPA, a la que lleva el botón "Ir a la web".</summary>
    private const string WebLoginPath = "login";

    public Task<Result> ProcessContactAsync(Guid contactId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "ProcessWhatsAppContact", () =>
            // El límite se abre antes de tomar la fila del contacto: la fila, el lock de la cuenta, la cuenta nueva, los
            // procesados, el enlace y los vínculos van en una sola transacción. TooManyLinks, Disabled y NotInvited son
            // respuestas exitosas que confirman los procesados; si la cola no toma la respuesta, ProcessCoreAsync lanza
            // y no queda nada.
            unitOfWork.ExecuteInTransactionAsync(
                ct => ProcessCoreAsync(contactId, ct), CommitPolicy.OnSuccess, cancellationToken));

    private async Task<Result> ProcessCoreAsync(Guid contactId, CancellationToken cancellationToken)
    {
        // Primero el lock: otra instancia que tome el mismo contacto espera acá o lo saltea, y nunca lee los mismos
        // mensajes. Si lo tiene otra, sus mensajes quedan pendientes para la próxima vuelta.
        var contact = await contacts.GetForProcessingAsync(contactId, cancellationToken);

        if (contact is null)
        {
            return Result.Success();
        }

        var pending = await messages.ListPendingInboundAsync(contact.Id, cancellationToken);

        if (pending.Count == 0)
        {
            return Result.Success();
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // Todos quedan procesados, respondidos o no, en la misma transacción que la respuesta: si algo falla, la unidad
        // de trabajo no guarda nada y siguen pendientes.
        foreach (var message in pending)
        {
            message.MarkProcessed(nowUtc);
        }

        if (DecidingMessage(pending, nowUtc) is not { } deciding)
        {
            LogNothingToAnswer(logger, pending.Count);

            return Result.Success();
        }

        // El número del chat: al que se responde y con el que se busca la cuenta. Sin él (cuando WhatsApp oculte los
        // números, un contacto puede llegar solo con el BSUID) no hay a quién mandarle nada.
        if (contact.WaId is not { } waId || phoneNumbers.FromWhatsAppId(waId) is not { IsSuccess: true } phone)
        {
            LogNoNumberToAnswer(logger, pending.Count);

            return Result.Success();
        }

        var reply = await DecideAsync(contact, phone.Value, deciding.ReplyId, cancellationToken);

        // La cola nunca espera. Si no toma la respuesta, se falla: la unidad de trabajo no guarda nada (ni el enlace, ni
        // la cuenta nueva, ni los mensajes procesados) y el procesador lo vuelve a intentar. Marcarlos procesados sería
        // dejar a la persona sin respuesta.
        if (!outbox.TryEnqueue(reply))
        {
            throw new InvalidOperationException(
                "The WhatsApp queue did not take the bot reply; the messages stay pending for the next round.");
        }

        LogAnswered(logger, reply.GetType().Name, pending.Count);

        return Result.Success();
    }

    /// <summary>
    /// El mensaje que decide la respuesta: el más nuevo que tenga algo que decidir, y un botón gana sobre un texto. No
    /// deciden los de hace más de 24 horas (ya no se les puede contestar), los avisos de WhatsApp (como el cambio de
    /// número, que queda para producción) ni «No pedí un código». Una foto, un audio o un sticker cuentan como un texto.
    /// </summary>
    private static WhatsAppMessage? DecidingMessage(IReadOnlyList<WhatsAppMessage> pending, DateTime nowUtc)
    {
        var answerable = pending
            .Where(message => message.OccurredAtUtc > nowUtc - ReplyWindow)
            .Where(message => message.Kind is not WhatsAppMessageKind.System)
            .Where(message => message.ReplyId is not BotButtons.DidNotRequestCode)
            .ToList();

        return answerable.Where(message => BotButtons.IsKnown(message.ReplyId)).MaxBy(message => message.OccurredAtUtc)
            ?? answerable.MaxBy(message => message.OccurredAtUtc);
    }

    /// <summary>La tabla de la sección 8 del spec, en orden: primero la cuenta del número y después el botón.</summary>
    private async Task<WhatsAppOutboundMessage> DecideAsync(
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
                ? await SendLoginLinkAsync(contact, phone, account, cancellationToken)
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
            BotButtons.CreateAccount when registrationOpen => await CreateAccountAsync(contact, phone, cancellationToken),
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

    /// <summary>
    /// La primera fila: emite un enlace, vincula el contacto a la cuenta y, si el número de la cuenta es el del chat y
    /// estaba sin verificar (lo cargó un administrador), lo verifica: la firma de Meta prueba que la persona escribe
    /// desde ese número (sección 4 del spec). Si pidió un enlace hace muy poco, no cambia nada.
    /// </summary>
    private async Task<WhatsAppOutboundMessage> SendLoginLinkAsync(
        WhatsAppContact contact,
        PhoneNumber phone,
        UserAccount account,
        CancellationToken cancellationToken)
    {
        var reply = Reply(account);

        // El lock de la cuenta ya lo tiene desde FindAccountAsync: el emisor lo vuelve a pedir y pasa de largo.
        var issued = await loginLinks.IssueAsync(account.Id, cancellationToken);

        if (issued.IsFailure)
        {
            return TooManyLinks(issued.Error, reply, phone);
        }

        await contactLinker.LinkAsync(contact, account.Id, cancellationToken);

        if (account.PhoneNumber == phone.Value && !account.PhoneNumberConfirmed)
        {
            await userRepository.SetPhoneAsync(account.Id, phone, confirmed: true, cancellationToken);
        }

        return reply.SignIn(phone, account.DisplayName ?? contact.ProfileName, issued.Value.Url);
    }

    /// <summary>
    /// «Crear cuenta» con el registro abierto: una cuenta sin correo, con el número verificado y el nombre del perfil de
    /// WhatsApp, que después se cambia en Mi perfil. El idioma es español, como todo lo que dice el bot sin cuenta: el
    /// de la petición no existe, porque esto corre en segundo plano.
    /// </summary>
    private async Task<WhatsAppOutboundMessage> CreateAccountAsync(
        WhatsAppContact contact,
        PhoneNumber phone,
        CancellationToken cancellationToken)
    {
        var account = await userRepository.CreateAsync(
            email: null, phone, phoneConfirmed: true, contact.ProfileName, UserCultures.Default, cancellationToken);
        var reply = Reply(account);
        var issued = await loginLinks.IssueAsync(account.Id, cancellationToken);

        if (issued.IsFailure)
        {
            return TooManyLinks(issued.Error, reply, phone);
        }

        await contactLinker.LinkAsync(contact, account.Id, cancellationToken);

        LogAccountCreated(logger);

        return reply.AccountCreated(phone, account.DisplayName, issued.Value.Url);
    }

    /// <summary>El emisor solo rechaza por sus límites (uno por minuto y 5 cada 15 minutos). Otro error es un bug.</summary>
    private static WhatsAppTextMessage TooManyLinks(Error error, BotReply reply, PhoneNumber phone) =>
        error.Code == LoginLinkErrors.TooManyRequestsCode
            ? reply.TooManyLinks(phone)
            : throw new InvalidOperationException($"Unexpected error issuing a login link: {error.Code}.");

    private BotReply Reply(UserAccount? account) => BotReply.For(appName.Value, account);

    private string WebLoginUrl()
    {
        var origin = publicOrigin.Value
            ?? throw new InvalidOperationException(
                "Authentication:Issuer must be set to the public origin of the web app to answer on WhatsApp.");

        return new Uri(origin, WebLoginPath).AbsoluteUri;
    }

    // Ningún log del bot lleva el texto de un mensaje, el enlace, el número ni el BSUID: solo cuántos y qué se hizo.
    [LoggerMessage(Level = LogLevel.Information, Message = "The WhatsApp bot answered {PendingMessages} pending messages with a {ReplyType}")]
    private static partial void LogAnswered(ILogger logger, string replyType, int pendingMessages);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The WhatsApp bot marked {PendingMessages} pending messages as processed without an answer: they are older than 24 hours, WhatsApp notices or 'did not request a code'")]
    private static partial void LogNothingToAnswer(ILogger logger, int pendingMessages);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The WhatsApp bot marked {PendingMessages} pending messages as processed without an answer: the contact has no valid number to answer to")]
    private static partial void LogNoNumberToAnswer(ILogger logger, int pendingMessages);

    [LoggerMessage(Level = LogLevel.Information, Message = "The WhatsApp bot created an account for a new number with open registration")]
    private static partial void LogAccountCreated(ILogger logger);
}
