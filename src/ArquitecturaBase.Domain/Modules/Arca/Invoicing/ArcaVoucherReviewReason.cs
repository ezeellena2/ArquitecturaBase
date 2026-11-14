namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>Por qué una emisión espera a una persona.</summary>
public enum ArcaVoucherReviewReason
{
    /// <summary>El número existe en ARCA con otros datos: otro sistema usa el punto de venta.</summary>
    NumberTaken,

    /// <summary>El número no está en ARCA, pero el último autorizado ya es ese o uno mayor.</summary>
    NumberMissing,

    /// <summary>La fecha pedida ya no entra en la ventana que acepta ARCA.</summary>
    DateOutOfRange,

    /// <summary>
    /// ARCA no aceptó la fecha con el número que le tocaba (10016): es anterior a la del último comprobante del mismo tipo y
    /// punto de venta. El número quedó libre.
    /// </summary>
    DateBeforeLastVoucher,
}
