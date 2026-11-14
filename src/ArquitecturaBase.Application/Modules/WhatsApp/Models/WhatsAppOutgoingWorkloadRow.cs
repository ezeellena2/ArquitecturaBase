namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>H9: what waits in the durable outgoing right now, for its gauges: the pending, the uncertain and the oldest pending.</summary>
public sealed record WhatsAppOutgoingWorkloadRow(int Pending, int Uncertain, DateTime? OldestPendingAtUtc);
