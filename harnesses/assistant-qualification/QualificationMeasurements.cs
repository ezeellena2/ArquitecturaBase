using System.Diagnostics.Metrics;

namespace AssistantQualification;

internal sealed record MeasurementSnapshot(int? MaximumContextBytes, int? MaximumContinuationBytes);

internal sealed class QualificationMeasurements : IDisposable
{
    private readonly object gate = new();
    private readonly MeterListener listener = new();
    private bool active;
    private int? maximumContextBytes;
    private int? maximumContinuationBytes;

    public QualificationMeasurements()
    {
        listener.InstrumentPublished = (instrument, observed) =>
        {
            if (instrument.Meter.Name == "ArquitecturaBase.AiAssistant" &&
                instrument.Name is "ai.assistant.context.bytes" or "ai.assistant.continuation.bytes")
                observed.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((instrument, bytes, _, _) =>
        {
            lock (gate)
            {
                if (!active) return;
                if (instrument.Name == "ai.assistant.context.bytes") maximumContextBytes = Math.Max(maximumContextBytes ?? 0, bytes);
                else maximumContinuationBytes = Math.Max(maximumContinuationBytes ?? 0, bytes);
            }
        });
        listener.Start();
    }

    public void BeginCase()
    {
        lock (gate) { maximumContextBytes = null; maximumContinuationBytes = null; active = true; }
    }

    public MeasurementSnapshot EndCase()
    {
        lock (gate) { active = false; return new(maximumContextBytes, maximumContinuationBytes); }
    }

    public void Dispose() => listener.Dispose();
}
