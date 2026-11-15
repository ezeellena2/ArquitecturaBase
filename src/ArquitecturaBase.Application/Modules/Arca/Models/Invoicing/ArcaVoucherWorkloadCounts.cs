namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// H26: cómo está la cola de emisión, para la métrica: los comprobantes por emitir (en cola, enviándose o sin saber si se
/// emitieron), los que esperan una revisión y cuándo entró el más viejo por emitir.
/// </summary>
public sealed record ArcaVoucherWorkloadCounts(int Pending, int AwaitingReview, DateTime? OldestPendingAtUtc);
