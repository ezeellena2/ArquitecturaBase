using System.Diagnostics.Metrics;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>
/// H9: what each send ended in, for the dashboards, as the assistant counts its own (its <c>AddMeter</c> is in the module's
/// registration). The durable outgoing counts by outcome (<c>sent</c>, <c>failed</c>, <c>uncertain</c>, <c>retry</c> when it
/// waits for its next attempt, <c>returned</c> when it went back without its turn or with the shutdown) and origin; the access
/// queue by outcome (<c>sent</c>, <c>failed</c>, <c>uncertain</c>, <c>retry</c>, <c>turnUnavailable</c>, <c>expired</c>). What
/// waits right now is three gauges, from the snapshot the outgoing worker takes once a minute: the pending, the uncertain and the
/// age of the oldest pending. No tag carries a number, a body or an id.
/// </summary>
internal static class WhatsAppMetrics
{
    public const string MeterName = "ArquitecturaBase.WhatsApp";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Outgoing = Meter.CreateCounter<long>("whatsapp.outgoing.outcomes");
    private static readonly Counter<long> Access = Meter.CreateCounter<long>("whatsapp.access.outcomes");
    private static Workload workload = new(0, 0, 0);

    static WhatsAppMetrics()
    {
        Meter.CreateObservableGauge("whatsapp.outgoing.pending", () => Volatile.Read(ref workload).Pending);
        Meter.CreateObservableGauge("whatsapp.outgoing.uncertain", () => Volatile.Read(ref workload).Uncertain);
        Meter.CreateObservableGauge("whatsapp.outgoing.pending.oldest.age", () => Volatile.Read(ref workload).OldestPendingAgeSeconds, "s");
    }

    public static void ObserveWorkload(WhatsAppOutgoingWorkloadRow snapshot, DateTime nowUtc) => Interlocked.Exchange(ref workload,
        new(snapshot.Pending, snapshot.Uncertain, snapshot.OldestPendingAtUtc is { } oldest ? Math.Max(0, (nowUtc - oldest).TotalSeconds) : 0));

    private sealed record Workload(int Pending, int Uncertain, double OldestPendingAgeSeconds);

    public static void RecordOutgoing(string outcome, WhatsAppMessageOrigin origin) =>
        Outgoing.Add(1, new KeyValuePair<string, object?>("outcome", outcome), new KeyValuePair<string, object?>("origin", origin.ToString()));

    public static void RecordAccess(string outcome) => Access.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
