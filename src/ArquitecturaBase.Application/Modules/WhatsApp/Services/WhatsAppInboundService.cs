using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>Processes inbound messages under the contact lock. Only structured access actions receive public instructions.</summary>
internal sealed partial class WhatsAppInboundService(
    IWhatsAppContactRepository contacts,
    IWhatsAppMessageRepository messages,
    IPhoneNumberParser phoneNumbers,
    WhatsAppAccessReplyPolicy replies,
    IWhatsAppSendQueue sendQueue,
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
            var classification = WhatsAppInboundPolicy.Classify(message.Kind, message.ReplyId);
            message.Classify(classification.Purpose, classification.EventKind);
            message.MarkProcessed(nowUtc);
        }

        if (WhatsAppInboundPolicy.SelectAction(pending, nowUtc) is not { } deciding)
        {
            LogNothingToAnswer(logger, pending.Count);

            return Result.Success();
        }

        // El número del chat: al que se envía orientación pública. Sin él (cuando WhatsApp oculte los
        // números, un contacto puede llegar solo con el BSUID) no hay a quién mandarle nada.
        if (contact.WaId is not { } waId || WhatsAppIds.Parse(phoneNumbers, waId) is not { IsSuccess: true } phone)
        {
            LogNoNumberToAnswer(logger, pending.Count);

            return Result.Success();
        }

        var reply = await replies.DecideAsync(phone.Value, deciding.ReplyId!, cancellationToken);

        // A rejected enqueue rolls back processed markers, so the action can be recovered.
        if (!sendQueue.TryEnqueue(reply))
        {
            throw new InvalidOperationException(
                "The WhatsApp queue did not take the access instructions; the messages stay pending for the next round.");
        }

        LogAnswered(logger, reply.GetType().Name, pending.Count);

        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The WhatsApp inbound processor answered {PendingMessages} pending messages with a {ReplyType}")]
    private static partial void LogAnswered(ILogger logger, string replyType, int pendingMessages);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The WhatsApp inbound processor marked {PendingMessages} pending messages as processed without an answer: no current access action requests instructions")]
    private static partial void LogNothingToAnswer(ILogger logger, int pendingMessages);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The WhatsApp inbound processor marked {PendingMessages} pending messages as processed without an answer: the contact has no valid number to answer to")]
    private static partial void LogNoNumberToAnswer(ILogger logger, int pendingMessages);
}
