using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// EM2-A: a tap on a form action button goes to its owners, which decide in their own transaction; WhatsApp only marks the tap
// as handled, with the same marker as any other handled tap. Nothing is sent back to the contact.
internal sealed partial class WhatsAppFormActionReplyService(IWhatsAppFormActionReader reader, IWhatsAppMessageRepository messages,
    IUnitOfWork unitOfWork, WhatsAppFormActionReplyPolicy policy, IEnumerable<IStructuredRecordActionReplySink> owners,
    TimeProvider clock, ILogger<WhatsAppFormActionReplyService> logger) : IWhatsAppFormActionReplyService
{
    private const int MaxTaps = 20;

    public Task<Result> ProcessAsync(Guid contactId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "ProcessWhatsAppActionReplies", async () =>
        {
            var taps = await reader.ListPendingRepliesAsync(contactId, clock.GetUtcNow().UtcDateTime, MaxTaps, cancellationToken);
            if (taps.Count == 0) return Result.Success();
            // Oldest first: of two taps on the same message the first decides, and the owner ignores the repeat.
            foreach (var tap in taps)
            {
                var target = WhatsAppFormActionChoice.TryParseReplyId(tap.ReplyId, out var outgoingId, out _)
                    ? await reader.FindReplyTargetAsync(outgoingId, cancellationToken) : null;
                if (await policy.ResolveAsync(tap, target, cancellationToken) is not { } reply)
                {
                    LogIgnored(logger);
                    continue;
                }

                var applied = 0;
                foreach (var owner in owners)
                {
                    if (await owner.ReplyAsync(reply.RunId, reply.RecipientUserId, reply.ChoiceId, cancellationToken) == StructuredRecordActionReplyState.Applied)
                        applied++;
                }

                LogHandedOver(logger, applied);
            }

            // An owner that throws leaves every tap unmarked: the next round hands them over again and the owner ignores repeats.
            return await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var nowUtc = clock.GetUtcNow().UtcDateTime;
                foreach (var tap in taps)
                {
                    if (await messages.GetInboundAsync(tap.WaMessageId, ct) is { } message && message.Id == tap.Id && message.ContactId == contactId)
                        message.MarkAssistantProcessed(nowUtc);
                }

                return Result.Success();
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    [LoggerMessage(Level = LogLevel.Information,
        Message = "A WhatsApp tap on a form action button was ignored: unknown or stale button, another contact or an unreachable recipient")]
    private static partial void LogIgnored(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "A WhatsApp tap on a form action button was handed to its owners; {Applied} applied it")]
    private static partial void LogHandedOver(ILogger logger, int applied);
}
