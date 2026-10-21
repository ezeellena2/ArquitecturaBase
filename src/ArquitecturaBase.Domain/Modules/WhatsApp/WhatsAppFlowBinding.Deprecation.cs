using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public enum WhatsAppFlowDeprecationState { Pending, Uncertain, Deprecated }

public sealed partial class WhatsAppFlowBinding
{
    public WhatsAppFlowDeprecationState DeprecationState { get; private set; }
    public long DeprecationFence { get; private set; }
    public DateTime? DeprecationLeaseExpiresAtUtc { get; private set; }
    public DateTime? DeprecationNextCheckAtUtc { get; private set; }
    public bool DeprecationInFlight { get; private set; }
    public DateTime? DeprecatedAtUtc { get; private set; }

    public Result<long> ReserveDeprecation(DateTime nowUtc, TimeSpan leaseDuration)
    {
        if (State != WhatsAppFlowPublicationState.Published || MetaFlowId is null
            || DeprecationState == WhatsAppFlowDeprecationState.Deprecated || DeprecationLeaseExpiresAtUtc > nowUtc
            || DeprecationNextCheckAtUtc > nowUtc || DeprecationFence == long.MaxValue || leaseDuration <= TimeSpan.Zero)
            return FlowErrors.StateConflict;
        if (DeprecationInFlight) DeprecationState = WhatsAppFlowDeprecationState.Uncertain;
        DeprecationFence++;
        DeprecationLeaseExpiresAtUtc = nowUtc.Add(leaseDuration);
        return DeprecationFence;
    }

    public bool OwnsDeprecationLease(long fence, DateTime nowUtc) =>
        State == WhatsAppFlowPublicationState.Published && DeprecationState != WhatsAppFlowDeprecationState.Deprecated
        && DeprecationFence == fence && DeprecationLeaseExpiresAtUtc > nowUtc;

    public Result MarkDeprecationStarted(long fence, DateTime nowUtc)
    {
        if (!OwnsDeprecationLease(fence, nowUtc) || DeprecationState != WhatsAppFlowDeprecationState.Pending || DeprecationInFlight)
            return FlowErrors.StateConflict;
        DeprecationInFlight = true;
        return Result.Success();
    }

    public Result RecordDeprecationEffect(long fence, DateTime nowUtc, bool success, bool uncertain)
    {
        if (!OwnsDeprecationLease(fence, nowUtc) || (success || uncertain) && !DeprecationInFlight)
            return FlowErrors.StateConflict;
        if (uncertain)
        {
            DeprecationState = WhatsAppFlowDeprecationState.Uncertain;
            DeprecationInFlight = true;
        }
        else if (success) ConfirmDeprecation(nowUtc);
        else if (DeprecationState != WhatsAppFlowDeprecationState.Uncertain)
        {
            DeprecationState = WhatsAppFlowDeprecationState.Pending;
            DeprecationInFlight = false;
        }
        ReleaseDeprecation(nowUtc);
        return Result.Success();
    }

    public Result RecordDeprecationVerification(long fence, DateTime nowUtc, bool identityMatches, bool published, bool deprecated)
    {
        if (!OwnsDeprecationLease(fence, nowUtc) || DeprecationState != WhatsAppFlowDeprecationState.Uncertain)
            return FlowErrors.StateConflict;
        if (identityMatches && deprecated && !published) ConfirmDeprecation(nowUtc);
        else if (identityMatches && published && !deprecated)
        {
            DeprecationState = WhatsAppFlowDeprecationState.Pending;
            DeprecationInFlight = false;
        }
        else
        {
            DeprecationState = WhatsAppFlowDeprecationState.Uncertain;
            DeprecationInFlight = true;
        }
        ReleaseDeprecation(nowUtc);
        return Result.Success();
    }

    public void DeferDeprecationCheck(DateTime nowUtc)
    {
        if (State == WhatsAppFlowPublicationState.Published && DeprecationState != WhatsAppFlowDeprecationState.Deprecated
            && !(DeprecationLeaseExpiresAtUtc > nowUtc))
            DeprecationNextCheckAtUtc = nowUtc.AddMinutes(5);
    }

    private void ConfirmDeprecation(DateTime nowUtc)
    {
        DeprecationState = WhatsAppFlowDeprecationState.Deprecated;
        DeprecatedAtUtc = nowUtc;
        DeprecationInFlight = false;
    }

    private void ReleaseDeprecation(DateTime nowUtc)
    {
        DeprecationLeaseExpiresAtUtc = null;
        DeprecationNextCheckAtUtc = DeprecationState == WhatsAppFlowDeprecationState.Deprecated ? null
            : nowUtc.AddSeconds(DeprecationState == WhatsAppFlowDeprecationState.Uncertain ? 300 : 15);
    }
}
