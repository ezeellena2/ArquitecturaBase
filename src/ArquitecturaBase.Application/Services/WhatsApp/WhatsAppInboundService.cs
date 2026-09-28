using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Integrations.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.WhatsApp;

/// <summary>
/// El bot (sección 8 del spec del ingreso con WhatsApp). No tiene estado de conversación: decide con la cuenta del
/// número que escribe y con el botón que se tocó, y solo habla de esa cuenta. Toma el contacto con su lock, marca
/// procesados todos sus pendientes y les da una sola respuesta, en la misma transacción. Nunca abre una sesión ni emite
/// tokens (sección 5): lo máximo que produce es un enlace de un solo uso, mandado al mismo chat. La respuesta la decide
/// <see cref="WhatsAppReplyPolicy"/>, que busca la cuenta con su lock, y lo que se escribe para una cuenta (el enlace, el
/// alta) lo hace <see cref="WhatsAppLinkIssuer"/>: ninguno de los dos abre el límite, que abre este servicio, uno solo por
/// contacto.
/// </summary>
internal sealed partial class WhatsAppInboundService(
    IWhatsAppContactRepository contacts,
    IWhatsAppMessageRepository messages,
    IPhoneNumberParser phoneNumbers,
    WhatsAppReplyPolicy replies,
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

        var reply = await replies.DecideAsync(contact, phone.Value, deciding.ReplyId, cancellationToken);

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
}
