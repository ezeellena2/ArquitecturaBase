using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;

/// <summary>La configuración de Facturación: el punto de venta de web service que usa la plataforma.</summary>
public interface IArcaInvoicingSetupService
{
    Task<Result<ArcaInvoicingSettingsResponse>> GetAsync(CancellationToken cancellationToken);

    /// <summary>Consulta a ARCA los puntos de venta; los dados de baja no vienen.</summary>
    Task<Result<IReadOnlyList<ArcaPointOfSaleResponse>>> ListPointsOfSaleAsync(CancellationToken cancellationToken);

    /// <summary>Elige uno que ARCA ofrece y no está bloqueado, y vuelve a calcular el estado de la conexión.</summary>
    Task<Result<ArcaInvoicingSettingsResponse>> ChoosePointOfSaleAsync(int number, CancellationToken cancellationToken);
}
