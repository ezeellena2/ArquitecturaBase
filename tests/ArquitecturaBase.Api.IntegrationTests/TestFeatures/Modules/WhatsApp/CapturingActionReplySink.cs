using System.Collections.Concurrent;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

/// <summary>
/// A neutral owner of action replies: it records what the channel hands over and applies only the first reply of each run and
/// person, like an owner that ignores repeats. It imports no forms module and decides nothing else.
/// </summary>
internal sealed class CapturingActionReplySink : IStructuredRecordActionReplySink
{
    private readonly ConcurrentQueue<(Guid RunId, Guid RecipientUserId, string ChoiceId)> calls = new();
    private readonly ConcurrentDictionary<(Guid, Guid), string> decided = new();

    public IReadOnlyList<(Guid RunId, Guid RecipientUserId, string ChoiceId)> Calls => [.. calls];

    public Task<StructuredRecordActionReplyState> ReplyAsync(Guid runId, Guid recipientUserId, string choiceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        calls.Enqueue((runId, recipientUserId, choiceId));
        return Task.FromResult(decided.TryAdd((runId, recipientUserId), choiceId)
            ? StructuredRecordActionReplyState.Applied : StructuredRecordActionReplyState.Ignored);
    }
}
