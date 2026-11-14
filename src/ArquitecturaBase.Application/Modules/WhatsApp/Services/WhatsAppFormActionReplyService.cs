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

// EM2-A: a tap on a form action button goes to its owners, which decide in their own transaction; WhatsApp marks the tap as
// handled, with the same marker as any other handled tap. The text an owner hands back with an applied tap («No la emitimos.
// FAC-000012 quedó anulada.») is queued to the chat in the transaction that marks it, by the tap (its request), so it goes out
// once. H21: each tap is its own call, its answer queued as soon as its owners return; the owner gets the tap's id and answers that
// same tap the same, so one handed over again after a cut is still answered.
internal sealed partial class WhatsAppFormActionReplyService(IWhatsAppFormActionReader reader,
    IWhatsAppFormActionRepository actions, IUnitOfWork unitOfWork, WhatsAppFormActionReplyPolicy policy,
    IEnumerable<IStructuredRecordActionReplySink> owners, TimeProvider clock, ILogger<WhatsAppFormActionReplyService> logger)
    : IWhatsAppFormActionReplyService
{
    private const int MaxTaps = 20;

    // Oldest first: of two taps on the same message the first decides, and the owner ignores the other.
    public async Task<IReadOnlyList<Guid>> ListPendingAsync(Guid contactId, CancellationToken cancellationToken) =>
        [.. (await reader.ListPendingRepliesAsync(contactId, clock.GetUtcNow().UtcDateTime, MaxTaps, cancellationToken)).Select(tap => tap.Id)];

    public Task<Result> ProcessAsync(Guid contactId, Guid tapId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "ProcessWhatsAppActionReply", async () =>
        {
            // Another round, or another copy, may have handled it since it was listed.
            var tap = (await reader.ListPendingRepliesAsync(contactId, clock.GetUtcNow().UtcDateTime, MaxTaps, cancellationToken))
                .SingleOrDefault(item => item.Id == tapId);
            if (tap is null) return Result.Success();
            var target = WhatsAppFormActionChoice.TryParseReplyId(tap.ReplyId, out var outgoingId, out _)
                ? (await reader.ListReplyTargetsAsync([outgoingId], cancellationToken)).SingleOrDefault() : null;
            var reply = await policy.ResolveAsync(tap, target, cancellationToken);
            // The port answers once per tap: the first text of an owner that applied it.
            string? text = null;
            if (reply is null) LogIgnored(logger);
            else
            {
                var applied = 0;
                foreach (var owner in owners)
                {
                    var answer = await owner.ReplyAsync(reply.RunId, reply.RecipientUserId, reply.ChoiceId, tap.Id, cancellationToken);
                    if (answer.State != StructuredRecordActionReplyState.Applied) continue;
                    applied++;
                    text ??= answer.Message;
                }

                LogHandedOver(logger, applied);
            }

            // An owner that throws, or a cut before this commit, leaves the tap unmarked: the next round hands it over again and the
            // owner answers that same tap the same.
            return await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var nowUtc = clock.GetUtcNow().UtcDateTime;
                // The contact goes first, as in every send; the answer leaves by the normal outgoing with the checks of any notice.
                var contact = reply is not null && text is not null ? await actions.GetContactForUpdateAsync(reply.RecipientUserId, ct) : null;
                var taps = await actions.ListTapsAsync(contactId, [tap.Id], ct);
                // Another copy handed over the same tap and answered it while this one waited for the contact.
                if (taps.Any(message => message.AssistantProcessedAtUtc is not null)) return Result.Success();
                if (reply is not null && text is not null)
                {
                    if (await policy.AnswerAsync(contact, tap, reply, text, nowUtc, ct) is { } outgoing) actions.Add(outgoing);
                    else LogNotAnswered(logger);
                }

                foreach (var message in taps) message.MarkAssistantProcessed(nowUtc);
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
