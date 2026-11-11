using System.Collections.Concurrent;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

/// <summary>
/// A neutral owner of action replies: it records what the channel hands over and applies only the first reply of each run and
/// person, like an owner that ignores repeats, handing back the text set for its choice, if any. It imports no forms module and
/// decides nothing else.
/// </summary>
internal sealed class CapturingActionReplySink : IStructuredRecordActionReplySink
{
    private readonly ConcurrentQueue<(Guid RunId, Guid RecipientUserId, string ChoiceId)> calls = new();
    private readonly ConcurrentDictionary<(Guid, Guid), string> decided = new();

    public IReadOnlyList<(Guid RunId, Guid RecipientUserId, string ChoiceId)> Calls => [.. calls];

    /// <summary>The text handed back when a reply with this choice is applied.</summary>
    public ConcurrentDictionary<string, string> Texts { get; } = new();

    public Task<StructuredRecordActionReply> ReplyAsync(Guid runId, Guid recipientUserId, string choiceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        calls.Enqueue((runId, recipientUserId, choiceId));
        return Task.FromResult(decided.TryAdd((runId, recipientUserId), choiceId)
            ? new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied, Texts.GetValueOrDefault(choiceId))
            : new StructuredRecordActionReply(StructuredRecordActionReplyState.Ignored));
    }
}
