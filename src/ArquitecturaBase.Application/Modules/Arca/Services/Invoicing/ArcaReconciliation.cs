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

    /// <summary>ARCA no lo tiene, pero el último autorizado ya es ese o uno mayor.</summary>
    Missing,
}
