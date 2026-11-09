using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>Una secuencia de numeración: ambiente, CUIT, punto de venta y tipo.</summary>
public sealed record ArcaVoucherSequenceRow(ArcaEnvironment Environment, string Cuit, int PointOfSale, int VoucherType)
{
    public override string ToString() => $"ArcaVoucherSequenceRow {{ environment: {Environment}, pointOfSale: {PointOfSale}, type: {VoucherType} }}";
}
