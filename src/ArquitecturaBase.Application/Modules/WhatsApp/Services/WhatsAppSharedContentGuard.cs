using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// W3: the open sends that can take what the person shared, before <see cref="WhatsAppSharedContentPolicy.Resolve"/> decides. A send
/// whose projection, payload or previous shared values cannot be read takes nothing, logged by the type of the failure and never with
/// a value. Its first message passes the reservation's own checks (<see cref="WhatsAppOutgoingSendGuard"/>), since the resend continues
/// it from the same sender and with its seal (A6): an operator who is inactive or lost whatsapp.read or whatsapp.send, or an assistant
/// whose context no longer holds, leaves the send out, so what was shared goes to the next one or stays in the chat instead of a resend
/// that would fail. It also seals what a send took, protected for it.
/// </summary>
internal sealed partial class WhatsAppSharedContentGuard(IWhatsAppFlowProtector projections, IWhatsAppFlowSessionProtector protector,
    WhatsAppOutgoingSendGuard reservation, ILogger<WhatsAppSharedContentGuard> logger)
{
    public async Task<IReadOnlyList<WhatsAppSharedContentSend>> ListUsableAsync(WhatsAppContact contact, IReadOnlyList<WhatsAppFlowOpenSend> open,
        CancellationToken cancellationToken)
    {
        var usable = new List<WhatsAppSharedContentSend>();
        foreach (var send in open)
            if (Readable(send) is { } readable && (await reservation.ValidateAsync(contact, send.First, cancellationToken)).IsSuccess)
                usable.Add(readable);
        return usable;
    }

    /// <summary>What the send took before, except what this pass wrote again in the same questions, and what it took now, protected for it.</summary>
    public void Seal(WhatsAppSharedContentSend send, IReadOnlyList<WhatsAppFlowSharedValue> taken)
    {
        var session = send.Session;
        session.SealSharedValues(protector.ProtectShared(session.Id, session.ContactId, session.UserId, session.VersionId, new WhatsAppFlowSharedValues(1,
            [.. send.Previous.Where(value => taken.All(written => written.FieldId != value.FieldId)), .. taken])));
    }

    // The projection says which questions receive what is shared, the payload the mode revision the send sealed, and the previous
    // values what the resend keeps.
    private WhatsAppSharedContentSend? Readable(WhatsAppFlowOpenSend send)
    {
        var session = send.Session;
        try
        {
            var projection = projections.ReadProjection(send.BindingOperationId, session.BindingId, session.VersionId, session.Culture, send.ProtectedProjection);
            var payload = protector.Read(session.Id, session.ContactId, session.UserId, session.VersionId, session.ProtectedPayload);
            IReadOnlyList<WhatsAppFlowSharedValue> previous = session.ProtectedSharedValues is { Length: > 0 } values
                ? protector.ReadShared(session.Id, session.ContactId, session.UserId, session.VersionId, values).Values : [];
            return projection.Culture == session.Culture ? new(session, send.First, projection, payload.ModeRevision) { Previous = previous } : null;
        }
        catch (Exception exception) when (exception is CryptographicException or InvalidOperationException or JsonException)
        {
            LogUnreadableSend(logger, exception.GetType().Name);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "An open WhatsApp Flow send could not be read for what the person shared; it takes nothing ({ExceptionType}).")]
    private static partial void LogUnreadableSend(ILogger logger, string exceptionType);
}
