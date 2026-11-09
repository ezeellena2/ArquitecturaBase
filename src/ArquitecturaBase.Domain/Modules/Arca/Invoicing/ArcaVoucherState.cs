namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>El estado de una emisión en la cola (ADR 0028).</summary>
public enum ArcaVoucherState
{
    /// <summary>En cola, sin número.</summary>
    Queued,

    /// <summary>Con número y hash guardados; el pedido a ARCA está en curso.</summary>
    Sending,

    /// <summary>ARCA lo autorizó: tiene CAE.</summary>
    Authorized,

    /// <summary>ARCA lo rechazó por una validación; el número quedó libre.</summary>
    Rejected,

    /// <summary>Con número y sin saber si se emitió: se reconcilia antes de volver a pedir.</summary>
    Uncertain,

    /// <summary>Lo tiene que decidir una persona; si tiene número, frena su secuencia.</summary>
    NeedsReview,

    /// <summary>No se emite: se canceló sin número o una persona decidió no emitir.</summary>
    Cancelled,
}
