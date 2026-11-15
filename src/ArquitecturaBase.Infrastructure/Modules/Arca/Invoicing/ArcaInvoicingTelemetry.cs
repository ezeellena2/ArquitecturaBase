using System.Diagnostics.Metrics;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;

/// <summary>
/// H26: la cola de emisión para los tableros, como la del asistente de IA: los comprobantes por emitir, los que esperan una
/// revisión y la antigüedad del más viejo por emitir. Los actualiza el worker, como mucho una vez por <c>PollInterval</c>.
/// </summary>
internal sealed class ArcaInvoicingTelemetry : IDisposable
{
    public const string MeterName = "ArquitecturaBase.Arca.Invoicing";

    private readonly Meter meter = new(MeterName, "1.0");
    private readonly TimeProvider timeProvider;
    private ArcaVoucherWorkloadCounts workload = new(0, 0, null);

    public ArcaInvoicingTelemetry(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
        meter.CreateObservableGauge("arca.vouchers.pending", () => Volatile.Read(ref workload).Pending);
        meter.CreateObservableGauge("arca.vouchers.review", () => Volatile.Read(ref workload).AwaitingReview);
        meter.CreateObservableGauge("arca.vouchers.pending.oldest.age", () => Age(Volatile.Read(ref workload).OldestPendingAtUtc), "s");
    }

    public void Observe(ArcaVoucherWorkloadCounts snapshot) => Interlocked.Exchange(ref workload, snapshot);

    public void Dispose() => meter.Dispose();

    private double Age(DateTime? sinceUtc) => sinceUtc is null ? 0 : Math.Max(0, (timeProvider.GetUtcNow().UtcDateTime - sinceUtc.Value).TotalSeconds);
}
