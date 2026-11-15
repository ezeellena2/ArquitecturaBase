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

    /// <summary>
    /// Lo que se mandaría de nuevo ya no es lo que se guardó con el número: la huella no coincide (H2). Conserva el número, porque
    /// ARCA puede tenerlo, y sigue con el siguiente.
    /// </summary>
    DataChanged,

    /// <summary>
    /// Al numerar, ARCA tiene un último autorizado mayor que el último número que la base conoce de la secuencia (H3): una
    /// restauración de la base u otro sistema en el punto de venta. Sin número; emitir vale para el último que vio la revisión.
    /// </summary>
    ArcaAhead,
}
