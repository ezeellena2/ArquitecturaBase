using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// EM2-A: a tap on a form action button goes to its owners, which decide in their own transaction; WhatsApp marks the tap as
// handled, with the same marker as any other handled tap. The text an owner hands back with an applied tap («No la emitimos.
// FAC-000012 quedó anulada.») is queued to the chat in the transaction that marks it: a tap handed over again finds the owner
// ignoring it, so it is never answered twice. A round reads the buttons of all its taps at once and marks them with one more read.
internal sealed partial class WhatsAppFormActionReplyService(IWhatsAppFormActionReader reader,
    IWhatsAppFormActionRepository actions, IUnitOfWork unitOfWork, WhatsAppFormActionReplyPolicy policy,
    IEnumerable<IStructuredRecordActionReplySink> owners, TimeProvider clock, ILogger<WhatsAppFormActionReplyService> logger)
    : IWhatsAppFormActionReplyService
{
    private const int MaxTaps = 20;

    public Task<Result> ProcessAsync(Guid contactId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "ProcessWhatsAppActionReplies", async () =>
        {
            var taps = await reader.ListPendingRepliesAsync(contactId, clock.GetUtcNow().UtcDateTime, MaxTaps, cancellationToken);
            if (taps.Count == 0) return Result.Success();
            var buttons = taps.Select(tap => WhatsAppFormActionChoice.TryParseReplyId(tap.ReplyId, out var outgoingId, out _) ? outgoingId : (Guid?)null)
                .OfType<Guid>().Distinct().ToArray();
            var targets = buttons.Length == 0 ? new Dictionary<Guid, WhatsAppFormActionReplyTargetRow>()
                : (await reader.ListReplyTargetsAsync(buttons, cancellationToken)).ToDictionary(target => target.OutgoingId);
            var answers = new List<(WhatsAppFormActionTapRow Tap, WhatsAppFormActionReply Reply, string Text)>();
            // Oldest first: of two taps on the same message the first decides, and the owner ignores the repeat.
            foreach (var tap in taps)
            {
                var target = WhatsAppFormActionChoice.TryParseReplyId(tap.ReplyId, out var outgoingId, out _)
                    ? targets.GetValueOrDefault(outgoingId) : null;
                if (await policy.ResolveAsync(tap, target, cancellationToken) is not { } reply)
                {
                    LogIgnored(logger);
                    continue;
                }

                var applied = 0;
                foreach (var owner in owners)
                {
                    var answer = await owner.ReplyAsync(reply.RunId, reply.RecipientUserId, reply.ChoiceId, cancellationToken);
                    if (answer.State != StructuredRecordActionReplyState.Applied) continue;
                    applied++;
                    if (answer.Message is { } text) answers.Add((tap, reply, text));
                }

                LogHandedOver(logger, applied);
            }

            // An owner that throws leaves every tap unmarked: the next round hands them over again and the owner ignores repeats.
            return await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var nowUtc = clock.GetUtcNow().UtcDateTime;
                // The contact goes first, as in every send; the answer leaves by the normal outgoing with the checks of any notice.
                foreach (var (tap, reply, text) in answers)
                {
                    var contact = await actions.GetContactForUpdateAsync(reply.RecipientUserId, ct);
                    if (await policy.AnswerAsync(contact, tap, reply, text, nowUtc, ct) is { } outgoing) actions.Add(outgoing);
                    else LogNotAnswered(logger);
                }

                foreach (var message in await actions.ListTapsAsync(contactId, [.. taps.Select(tap => tap.Id)], ct))
                    message.MarkAssistantProcessed(nowUtc);

                return Result.Success();
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    [LoggerMessage(Level = LogLevel.Information,
        Message = "A WhatsApp tap on a form action button was ignored: unknown or stale button, another contact or an unreachable recipient")]
    private static partial void LogIgnored(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "A WhatsApp tap on a form action button was handed to its owners; {Applied} applied it")]
    private static partial void LogHandedOver(ILogger logger, int applied);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "The text for an applied WhatsApp tap was not sent: the chat no longer reaches the person, the reply window closed or the text does not fit")]
    private static partial void LogNotAnswered(ILogger logger);
}
