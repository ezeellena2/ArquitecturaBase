using System.Text.Json;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// O49: a reply of the person that Meta could not deliver comes only as a <c>failed</c> status with 131052 («Unable to download the media
/// sent by the user») of a message that is not ours, with the person's number or BSUID; its <c>nfm_reply</c> never arrives. In the unit of
/// work of the webhook that keeps its receipt, each open send of that person (<see cref="IWhatsAppFlowSessionRepository.ListOpenAsync"/>)
/// whose form asks for a file, since only a reply with files is lost this way, and whose sender can still send it
/// (<see cref="WhatsAppSharedContentGuard"/> and <see cref="WhatsAppSharedContentPolicy.CanSend"/>) queues the resend of its form, as W3
/// does (<see cref="WhatsAppFlowLocationPolicy.Resend"/>), with the message that the reply did not arrive, unless the send did not exist
/// yet or the person already gets a new one (<see cref="SendsAgain"/>). Without the person or such a send nothing changes. It decides
/// and changes the entities; the caller locks the person first and saves, and nothing here calls Meta.
/// </summary>
internal sealed partial class WhatsAppFlowLostReplyPolicy(IWhatsAppContactRepository contacts, IWhatsAppConversationRepository conversations,
    IWhatsAppFlowSessionRepository sessions, WhatsAppSharedContentGuard guard, TimeProvider clock, ILogger<WhatsAppFlowLostReplyPolicy> logger)
{
    /// <summary>Meta's code of a media the person sent that it could not download.</summary>
    public const int MediaNotDownloaded = 131052;

    public static bool IsLost(WhatsAppWebhookStatus status) =>
        status is { Status: WhatsAppMessageStatus.Failed, ErrorCode: MediaNotDownloaded }
        && (status.RecipientUserIdentifier is not null || status.RecipientWaId is not null);

    /// <summary>A form loses its reply this way only if it asks for a photo or a document.</summary>
    public static bool AsksForFile(WhatsAppFlowProjection projection) => projection.Mapping.Any(field => field.Files is not null);

    /// <summary>The message of the resend in the language of the send, with its form as the send keeps it, or the first name it has.</summary>
    public static string? Body(string culture, string nameJson)
    {
        Dictionary<string, string>? names;
        try { names = JsonSerializer.Deserialize<Dictionary<string, string>>(nameJson, JsonSerializerOptions.Web); }
        catch (JsonException) { return null; }
        return names is not null && (names.TryGetValue(culture, out var name) ? name : names.Values.FirstOrDefault()) is { Length: > 0 } formName
            ? DisplayNumbers.Format(culture, FlowTexts.Get("LostReplyResend", culture), formName)
            : null;
    }

    /// <summary>
    /// Whether the person gets this open send again for a reply lost at <paramref name="lostAtUtc"/>: the send existed then, no resend of it
    /// waits in the outgoing or was requested after the loss (W3 or another lost reply already gives the person a new one) and, if it took
    /// something shared, it still keeps what was written, without which its resend does not go out (<see cref="WhatsAppFlowSessionVerifier"/>).
    /// </summary>
    public static bool SendsAgain(WhatsAppFlowOpenSend send, DateTime lostAtUtc) =>
        send.Session.CreatedAtUtc <= lostAtUtc && !send.ResendWaiting && !(send.LastResendAtUtc >= lostAtUtc)
        && (send.Session.SharedContentJson is null || send.Session.ProtectedSharedValues is { Length: > 0 });

    /// <summary>The resends of the open sends of the persons of these lost replies, each person once with its last loss.</summary>
    public async Task<int> QueueAsync(IReadOnlyList<WhatsAppWebhookStatus> lost, CancellationToken cancellationToken)
    {
        var persons = new Dictionary<Guid, DateTime>();
        foreach (var status in lost)
            if (await FindAsync(status.RecipientUserIdentifier, status.RecipientWaId, cancellationToken) is { } found)
                persons[found.Id] = persons.TryGetValue(found.Id, out var lostAtUtc) && lostAtUtc > status.OccurredAtUtc ? lostAtUtc : status.OccurredAtUtc;
        var resent = 0;
        // Their rows in the order of their ids, as a change of number takes them, and the open sends under the contact's lock, as W3 lists
        // them. The lock reloads a contact read just to find it; one this webhook already changed (a message of the same person) keeps it.
        foreach (var (contactId, lostAtUtc) in persons.OrderBy(person => person.Key))
        {
            if (await conversations.GetForUpdateAsync(contactId, cancellationToken) is not { } contact) continue;
            var nowUtc = clock.GetUtcNow().UtcDateTime;
            var open = (await sessions.ListOpenAsync(contactId, nowUtc, cancellationToken)).Where(send => SendsAgain(send, lostAtUtc)).ToArray();
            if (open.Length == 0) continue;
            foreach (var send in await guard.ListUsableAsync(contact, open, cancellationToken))
            {
                if (!AsksForFile(send.Projection) || !WhatsAppSharedContentPolicy.CanSend(send, contact)
                    || Body(send.Session.Culture, send.Session.NameJson) is not { } body) continue;
                conversations.Add(WhatsAppFlowLocationPolicy.Resend(send.Session, send.First, body, nowUtc));
                resent++;
            }
        }
        LogLost(logger, lost.Count, resent);
        return resent;
    }

    // As the webhook finds who sent a message: by the BSUID first and then by the number, which serves when one of the two has no BSUID
    // or, if this webhook just gave it that BSUID, the same one. A status creates nobody.
    private async Task<WhatsAppContact?> FindAsync(string? userIdentifier, string? waId, CancellationToken cancellationToken)
    {
        if (userIdentifier is not null && await contacts.GetByUserIdentifierAsync(userIdentifier, cancellationToken) is { } byUser) return byUser;
        return waId is not null && await contacts.GetLatestByWaIdAsync(waId, cancellationToken) is { } byNumber
            && (userIdentifier is null || byNumber.UserIdentifier is null || string.Equals(byNumber.UserIdentifier, userIdentifier, StringComparison.Ordinal))
            ? byNumber : null;
    }

    // Only how many: neither numbers, nor BSUID, nor forms.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Meta could not deliver {LostReplies} WhatsApp replies (131052); {Resent} open forms were sent again")]
    private static partial void LogLost(ILogger logger, int lostReplies, int resent);
}
