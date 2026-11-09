namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>Las tablas de parámetros de WSFEv1 que usa la factura.</summary>
public enum ArcaParameterTable
{
    /// <summary><c>FEParamGetCondicionIvaReceptor</c>: una fila por condición y clase de comprobante.</summary>
    ReceiverVatConditions,

    /// <summary><c>FEParamGetTiposDoc</c>.</summary>
    DocumentTypes,

    /// <summary><c>FEParamGetTiposConcepto</c>.</summary>
    Concepts,

    /// <summary><c>FEParamGetTiposMonedas</c>.</summary>
    Currencies,

    /// <summary><c>FEParamGetTiposIva</c>.</summary>
    VatRates,
}

/// <summary>
/// Una fila de una tabla de ARCA: el código, el texto tal cual y su vigencia («NULL» es sin fin). En la condición frente
/// al IVA del receptor, la clase de comprobante (A, B, C, M o 49) va en <paramref name="VoucherClass"/>.
/// </summary>
public sealed record ArcaParameter(string Id, string Description, DateOnly? ValidFrom, DateOnly? ValidTo, string? VoucherClass = null);
