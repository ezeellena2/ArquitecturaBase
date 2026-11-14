using System.Collections.Concurrent;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

/// <summary>
/// A neutral owner of action replies: it records what the channel hands over and applies only the first reply of each run and
/// person, like an owner that ignores repeats, handing back the text set for its choice, if any; the same tap handed again gets the
/// same answer (H21). It imports no forms module and decides nothing else.
/// </summary>
internal sealed class CapturingActionReplySink : IStructuredRecordActionReplySink
{
    private readonly ConcurrentQueue<(Guid RunId, Guid RecipientUserId, string ChoiceId)> calls = new();
    private readonly ConcurrentDictionary<(Guid, Guid), (Guid Tap, string Choice)> decided = new();

    public IReadOnlyList<(Guid RunId, Guid RecipientUserId, string ChoiceId)> Calls => [.. calls];

    /// <summary>The text handed back when a reply with this choice is applied.</summary>
    public ConcurrentDictionary<string, string> Texts { get; } = new();

    /// <summary>The next reply fails once, as an owner that cannot answer for a moment.</summary>
    public bool FailNext { get; set; }

    public Task<StructuredRecordActionReply> ReplyAsync(Guid runId, Guid recipientUserId, string choiceId, Guid tapId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        calls.Enqueue((runId, recipientUserId, choiceId));
        if (FailNext)
        {
            FailNext = false;
            throw new InvalidOperationException("The owner is unavailable.");
        }
        var kept = decided.GetOrAdd((runId, recipientUserId), (tapId, choiceId));
        return Task.FromResult(kept.Tap == tapId
            ? new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied, Texts.GetValueOrDefault(kept.Choice))
            : new StructuredRecordActionReply(StructuredRecordActionReplyState.Ignored));
    }
}
