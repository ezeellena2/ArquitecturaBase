using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>La configuración propia de Facturación: el punto de venta exclusivo de la plataforma.</summary>
public sealed class ArcaInvoicingSettings : Entity
{
    private ArcaInvoicingSettings() { }

    public Guid ConnectionId { get; private set; }
    public int? PointOfSale { get; private set; }
    public bool IsComplete => PointOfSale is not null;

    public static ArcaInvoicingSettings Create(Guid connectionId) => new() { ConnectionId = connectionId };

    public Result ChoosePointOfSale(int number)
    {
        if (number is < 1 or > 99998) return ArcaInvoicingErrors.PointOfSaleInvalid;
        PointOfSale = number;
        return Result.Success();
    }
}
