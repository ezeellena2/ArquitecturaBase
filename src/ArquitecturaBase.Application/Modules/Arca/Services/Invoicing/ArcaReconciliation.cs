namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>Lo que dice <c>FECompConsultar</c> sobre un número que se mandó sin saber si se emitió.</summary>
internal enum ArcaReconciliation
{
    /// <summary>ARCA lo tiene con nuestros datos: se emitió.</summary>
    Issued,

    /// <summary>ARCA lo tiene con otros datos: alguien usó ese número.</summary>
    Taken,

    /// <summary>ARCA no lo tiene y el último autorizado es menor: no se emitió y el número sigue siendo nuestro.</summary>
    NotIssued,

    /// <summary>
    /// Como <see cref="NotIssued"/>, pero ARCA no puede aceptar su fecha: es anterior a la del último comprobante autorizado o
    /// salió de la ventana. Reenviarlo daría un 10016: lo decide una persona, sin el número (ADR 0031).
    /// </summary>
    DateRefused,

    /// <summary>ARCA no lo tiene, pero el último autorizado ya es ese o uno mayor.</summary>
    Missing,
}
