using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// W1-A: under the contact's lock, the sends that wait for a location take the locations and «Seguir sin ubicación» taps that
/// arrived (<see cref="WhatsAppFlowLocationPolicy.Resolve"/>), and each resolved send queues its Flow in the durable outgoing of the
/// same unit of work. W3, after it: the open sends take the locations nothing took and the contacts
/// (<see cref="WhatsAppSharedContentPolicy.Resolve"/>) among those whose sender would pass the reservation and whose seals can be read
/// (<see cref="WhatsAppSharedContentGuard"/>), and each send that took something queues one resend. What no send takes stays a message
/// of the chat.
/// </summary>
internal sealed class WhatsAppFlowLocationService(IWhatsAppConversationRepository conversations, IWhatsAppFlowSessionRepository sessions,
    IWhatsAppMessageRepository messages, WhatsAppSharedContentGuard shared, IUnitOfWork unitOfWork, TimeProvider clock,
    ILogger<WhatsAppFlowLocationService> logger)
    : IWhatsAppFlowLocationService
{
    public Task<Result<int>> ProcessAsync(Guid contactId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<int>>(logger, "ResolveWhatsAppFlowLocations", () =>
            unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                if (await conversations.GetForUpdateAsync(contactId, ct) is not { } contact) return Result.Success(0);
                var nowUtc = clock.GetUtcNow().UtcDateTime;
                var waiting = await sessions.ListAwaitingLocationAsync(contactId, nowUtc, ct);
                // W3: the open sends are listed before W1-A resolves any, so a send whose Flow goes out in this pass takes nothing in it.
                var open = await sessions.ListOpenAsync(contactId, nowUtc, ct);
                if (waiting.Count == 0 && open.Count == 0) return Result.Success(0);
                var sinceUtc = waiting.Select(wait => wait.Session.CreatedAtUtc).Concat(open.Select(send => send.Session.CreatedAtUtc)).Min();
                var answers = await messages.ListLocationAnswersAsync(contactId, sinceUtc, ct);
                var resolved = WhatsAppFlowLocationPolicy.Resolve(waiting.Select(wait => wait.Session).ToArray(), answers, nowUtc);
                foreach (var session in resolved)
                    conversations.Add(WhatsAppFlowLocationPolicy.Flow(session, waiting.First(wait => wait.Session == session).Request, nowUtc));
                return Result.Success(resolved.Count + await ShareAsync(contact, open, answers, nowUtc, ct));
            }, CommitPolicy.OnSuccess, cancellationToken));

    // W3: one resend per send and pass, with everything the send took so far over its prefill, protected for the send.
    private async Task<int> ShareAsync(WhatsAppContact contact, IReadOnlyList<WhatsAppFlowOpenSend> open, IReadOnlyList<WhatsAppMessage> answers,
        DateTime nowUtc, CancellationToken ct)
    {
        if (open.Count == 0 || !answers.Any(answer => answer.FlowSessionId is null && answer.Kind is WhatsAppMessageKind.Location or WhatsAppMessageKind.Contact))
            return 0;
        var takes = WhatsAppSharedContentPolicy.Resolve(await shared.ListUsableAsync(contact, open, ct), answers, contact, nowUtc);
        foreach (var group in takes.GroupBy(take => take.Send))
        {
            shared.Seal(group.Key, [.. group.SelectMany(take => take.Values)]);
            conversations.Add(WhatsAppFlowLocationPolicy.Resend(group.Key.Session, group.Key.First,
                WhatsAppSharedContentPolicy.Body(group.Key.Session, group.ToArray()), nowUtc));
        }
        return takes.Count;
    }
}
