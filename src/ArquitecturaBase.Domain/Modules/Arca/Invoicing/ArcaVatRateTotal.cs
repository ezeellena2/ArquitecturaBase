namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>La base y el IVA de una alícuota, como van en <c>AlicIva</c> (<c>Id</c>, <c>BaseImp</c> e <c>Importe</c>).</summary>
public sealed record ArcaVatRateTotal(int RateId, decimal Base, decimal Vat);
