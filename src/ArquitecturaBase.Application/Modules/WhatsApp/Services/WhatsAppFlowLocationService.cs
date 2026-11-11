using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// W1-A: under the contact's lock, the sends that wait for a location take the locations and «Seguir sin ubicación» taps that
/// arrived (<see cref="WhatsAppFlowLocationPolicy.Resolve"/>), and each resolved send queues its Flow in the durable outgoing of the
/// same unit of work. A location no send waits for stays a message of the chat.
/// </summary>
internal sealed class WhatsAppFlowLocationService(IWhatsAppConversationRepository conversations, IWhatsAppFlowSessionRepository sessions,
    IWhatsAppMessageRepository messages, IUnitOfWork unitOfWork, TimeProvider clock, ILogger<WhatsAppFlowLocationService> logger)
    : IWhatsAppFlowLocationService
{
    public Task<Result<int>> ProcessAsync(Guid contactId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<int>>(logger, "ResolveWhatsAppFlowLocations", () =>
            unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                if (await conversations.GetForUpdateAsync(contactId, ct) is null) return Result.Success(0);
                var nowUtc = clock.GetUtcNow().UtcDateTime;
                var waiting = await sessions.ListAwaitingLocationAsync(contactId, nowUtc, ct);
                if (waiting.Count == 0) return Result.Success(0);
                var answers = await messages.ListLocationAnswersAsync(contactId, waiting.Min(wait => wait.Session.CreatedAtUtc), ct);
                var resolved = WhatsAppFlowLocationPolicy.Resolve(waiting.Select(wait => wait.Session).ToArray(), answers, nowUtc);
                foreach (var session in resolved)
                    conversations.Add(WhatsAppFlowLocationPolicy.Flow(session, waiting.First(wait => wait.Session == session).Request, nowUtc));
                return Result.Success(resolved.Count);
            }, CommitPolicy.OnSuccess, cancellationToken));
}
