using System.Collections.Concurrent;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class FlowDeprecationProbe
{
    private readonly ConcurrentDictionary<string, bool> _deprecated = new(StringComparer.Ordinal);
    private int _posts;
    private int _reads;
    public int Posts => Volatile.Read(ref _posts);
    public int Reads => Volatile.Read(ref _reads);
    public bool HoldNextPost { get; set; }
    public bool UncertainOnce { get; set; }
    public bool RejectOnce { get; set; }
    public bool ApplyRemoteEffect { get; set; } = true;
    public WhatsAppFlowRemoteState? ReadOverride { get; set; }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ConcurrentQueue<string> Events { get; } = new();

    public async Task<WhatsAppFlowEffectResponse> DeprecateAsync(string metaFlowId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _posts);
        Events.Enqueue("post:" + metaFlowId);
        var rejected = RejectOnce;
        RejectOnce = false;
        if (ApplyRemoteEffect && !rejected) _deprecated[metaFlowId] = true;
        if (HoldNextPost)
        {
            HoldNextPost = false;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
        var uncertain = UncertainOnce;
        UncertainOnce = false;
        return rejected ? new(false, false, null, [], FlowErrors.DeprecationRejected.Code)
            : new(!uncertain, uncertain, null, []);
    }

    public WhatsAppFlowRemoteState Read(string metaFlowId)
    {
        Interlocked.Increment(ref _reads);
        Events.Enqueue("get:" + metaFlowId);
        return ReadOverride ?? (_deprecated.ContainsKey(metaFlowId) ? WhatsAppFlowRemoteState.Deprecated : WhatsAppFlowRemoteState.Published);
    }
}
