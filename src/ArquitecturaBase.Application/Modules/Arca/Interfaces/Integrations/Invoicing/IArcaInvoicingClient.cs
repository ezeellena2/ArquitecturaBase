using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;

/// <summary>WSFEv1, el servicio de factura electrónica (<c>wsfe</c>).</summary>
public interface IArcaInvoicingClient
{
    /// <summary><c>FEDummy</c>, sin ticket: true si responden en «OK» el servidor de aplicación, el de datos y el de autenticación.</summary>
    Task<ArcaServiceReply<bool>> CheckServerAsync(ArcaEnvironment environment, CancellationToken cancellationToken);

    /// <summary><c>FEParamGetPtosVenta</c>: los puntos de venta de web service de la CUIT; sin resultados es una lista vacía.</summary>
    Task<ArcaServiceReply<IReadOnlyList<ArcaPointOfSale>>> ListPointsOfSaleAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket,
        CancellationToken cancellationToken);
}
