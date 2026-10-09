using System.Diagnostics.Metrics;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;

internal sealed class AiAssistantTelemetry : IDisposable
{
    private readonly Meter meter = new("ArquitecturaBase.AiAssistant.Maintenance", "1.0");
    private readonly TimeProvider timeProvider;
    private readonly Counter<long> maintenanceFailures;
    private AiAssistantWorkloadRow workload = new(0, 0, 0, null, null);

    public AiAssistantTelemetry(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
        maintenanceFailures = meter.CreateCounter<long>("ai.assistant.maintenance.failures");
        meter.CreateObservableGauge("ai.assistant.pending", () => Volatile.Read(ref workload).Pending);
        meter.CreateObservableGauge("ai.assistant.running", () => Volatile.Read(ref workload).Running);
        meter.CreateObservableGauge("ai.assistant.running.expired", () => Volatile.Read(ref workload).ExpiredRunning);
        meter.CreateObservableGauge("ai.assistant.accepted.today", () => Volatile.Read(ref workload).AcceptedToday);
        meter.CreateObservableGauge("ai.assistant.pending.oldest.age", () => Age(Volatile.Read(ref workload).OldestPendingAtUtc), "s");
        meter.CreateObservableGauge("ai.assistant.content.purge.overdue", () => Age(Volatile.Read(ref workload).OldestOverdueContentAtUtc), "s");
    }

    public void Observe(AiAssistantWorkloadRow snapshot) => Interlocked.Exchange(ref workload, snapshot);
    public void RecordMaintenanceFailure() => maintenanceFailures.Add(1);
    private double Age(DateTime? sinceUtc) => sinceUtc is null ? 0 : Math.Max(0, (timeProvider.GetUtcNow().UtcDateTime - sinceUtc.Value).TotalSeconds);
    public void Dispose() => meter.Dispose();
}
