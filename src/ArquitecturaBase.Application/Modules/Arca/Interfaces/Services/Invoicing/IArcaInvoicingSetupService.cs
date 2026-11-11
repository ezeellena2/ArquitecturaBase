using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;

/// <summary>
/// La configuración de Facturación: el punto de venta de web service que usa la plataforma y los datos del comprobante
/// que van en el PDF fiscal.
/// </summary>
public interface IArcaInvoicingSetupService
{
    Task<Result<ArcaInvoicingSettingsResponse>> GetAsync(CancellationToken cancellationToken);

    /// <summary>Consulta a ARCA los puntos de venta; los dados de baja no vienen.</summary>
    Task<Result<IReadOnlyList<ArcaPointOfSaleResponse>>> ListPointsOfSaleAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Elige uno que ARCA ofrece y no está bloqueado, con la revisión que se leyó, y vuelve a calcular el estado de la conexión.
    /// </summary>
    Task<Result<ArcaInvoicingSettingsResponse>> ChoosePointOfSaleAsync(ChooseArcaPointOfSaleRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Guarda ingresos brutos e inicio de actividades con la revisión que se leyó, sin llamar a ARCA ni cambiar el estado de la
    /// conexión.
    /// </summary>
    Task<Result<ArcaInvoicingSettingsResponse>> SetVoucherDataAsync(SetArcaVoucherDataRequest request, CancellationToken cancellationToken);
}
