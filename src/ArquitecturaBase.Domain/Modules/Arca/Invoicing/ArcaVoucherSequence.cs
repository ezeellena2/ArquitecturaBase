using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>
/// El candado de numeración de una CUIT, un punto de venta y un tipo (ADR 0028): una reserva corta con dueño, vencimiento y
/// fence para que un solo host trabaje la secuencia, y el comprobante pendiente, el que tiene número sin resolver. Mientras
/// haya pendiente nadie toma otro número: si ARCA no lo emitió, ese número sigue siendo suyo.
/// </summary>
public sealed class ArcaVoucherSequence : Entity
{
    private ArcaVoucherSequence() { }

    public Guid ConnectionId { get; private set; }
    public string Cuit { get; private set; } = string.Empty;
    public int PointOfSale { get; private set; }
    public int VoucherType { get; private set; }
    public Guid? LeaseOwner { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public long Fence { get; private set; }
    public Guid? PendingVoucherId { get; private set; }

    public static ArcaVoucherSequence Create(Guid connectionId, string cuit, int pointOfSale, int voucherType) =>
        new() { ConnectionId = connectionId, Cuit = cuit, PointOfSale = pointOfSale, VoucherType = voucherType };

    /// <summary>Toma la reserva con un fence nuevo, o null si otro host la tiene vigente.</summary>
    public long? TryReserve(Guid owner, DateTime nowUtc, TimeSpan duration)
    {
        if (LeaseOwner is { } current && current != owner && LeaseUntilUtc > nowUtc) return null;
        (LeaseOwner, LeaseUntilUtc) = (owner, nowUtc + duration);
        return ++Fence;
    }

    public bool Owns(Guid owner, long fence, DateTime nowUtc) => LeaseOwner == owner && Fence == fence && LeaseUntilUtc > nowUtc;

    public void Hold(Guid voucherId) => PendingVoucherId = voucherId;

    public void ReleasePending(Guid voucherId)
    {
        if (PendingVoucherId == voucherId) PendingVoucherId = null;
    }

    public bool Release(Guid owner, long fence)
    {
        if (LeaseOwner != owner || Fence != fence) return false;
        (LeaseOwner, LeaseUntilUtc) = (null, null);
        return true;
    }

    public override string ToString() => $"ArcaVoucherSequence {{ pointOfSale: {PointOfSale}, type: {VoucherType}, fence: {Fence} }}";
}
