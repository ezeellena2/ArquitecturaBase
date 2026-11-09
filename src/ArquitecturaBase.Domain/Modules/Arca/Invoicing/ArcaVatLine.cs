namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>
/// Un renglón para el desglose del IVA: su importe (cantidad por precio unitario, redondeado a 2 decimales con half-even) y
/// cómo lo trata el IVA. El importe incluye el IVA o no según el formulario.
/// </summary>
public readonly record struct ArcaVatLine(decimal Amount, ArcaVatTreatment Treatment);
