using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;

/// <summary>
/// La cola de emisión vista por quien pide (ADR 0028): encolar es idempotente por la clave del pedido y no llama a ARCA;
/// lo procesa el worker.
/// </summary>
public interface IArcaVoucherService
{
    Task<Result<ArcaVoucherResponse>> EnqueueAsync(ArcaVoucherOrder order, CancellationToken cancellationToken);

    /// <summary>La nota de crédito por el total de una factura autorizada, con la factura en <c>CbtesAsoc</c> y la fecha de hoy.</summary>
    Task<Result<ArcaVoucherResponse>> EnqueueCreditNoteAsync(Guid requestKey, Guid invoiceRequestKey, CancellationToken cancellationToken);

    /// <summary>La clase, el código y el total que saldrían, sin guardar nada.</summary>
    Task<Result<ArcaVoucherPreviewResponse>> PreviewAsync(ArcaVoucherOrder order, CancellationToken cancellationToken);
}
