using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;

/// <summary>
/// WSFEv1, el servicio de factura electrónica (<c>wsfe</c>). Ningún método lanza por una respuesta de ARCA ni reintenta: un
/// pedido que no llegó o no se entiende vuelve como <see cref="ArcaServiceCheckStatus.Unavailable"/>.
/// </summary>
public interface IArcaInvoicingClient
{
    /// <summary><c>FEDummy</c>, sin ticket: true si responden en «OK» el servidor de aplicación, el de datos y el de autenticación.</summary>
    Task<ArcaServiceReply<bool>> CheckServerAsync(ArcaEnvironment environment, CancellationToken cancellationToken);

    /// <summary><c>FEParamGetPtosVenta</c>: los puntos de venta de web service de la CUIT; sin resultados es una lista vacía.</summary>
    Task<ArcaServiceReply<IReadOnlyList<ArcaPointOfSale>>> ListPointsOfSaleAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket,
        CancellationToken cancellationToken);

    /// <summary><c>FECompUltimoAutorizado</c>: el último número autorizado de ese punto de venta y tipo (0 si no hay ninguno).</summary>
    Task<ArcaServiceReply<long>> GetLastAuthorizedAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket, int pointOfSale,
        int voucherType, CancellationToken cancellationToken);

    /// <summary>
    /// <c>FECAESolicitar</c> con un solo comprobante. Una respuesta que ARCA dio y se entiende vuelve lista, con el resultado
    /// (aprobado, rechazado o incierto); cualquier otra cosa no dice si se emitió.
    /// </summary>
    Task<ArcaServiceReply<ArcaAuthorization>> RequestAuthorizationAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket,
        ArcaVoucherRequest voucher, CancellationToken cancellationToken);

    /// <summary><c>FECompConsultar</c>: el comprobante que ARCA tiene con ese número, o null si no existe (602).</summary>
    Task<ArcaServiceReply<ArcaIssuedVoucher?>> FindVoucherAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket, int pointOfSale,
        int voucherType, long number, CancellationToken cancellationToken);

    /// <summary>Una tabla de parámetros de WSFEv1, con todas sus filas (las vencidas también: su vigencia dice cuáles valen).</summary>
    Task<ArcaServiceReply<IReadOnlyList<ArcaParameter>>> ListParametersAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket,
        ArcaParameterTable table, CancellationToken cancellationToken);
}
