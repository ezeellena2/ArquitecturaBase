namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>Qué se sabe después de <c>FECAESolicitar</c>.</summary>
public enum ArcaAuthorizationResult
{
    /// <summary>ARCA lo autorizó (con observaciones o sin ellas): hay CAE.</summary>
    Approved,

    /// <summary>ARCA lo rechazó por una validación: no se emitió y el número queda libre.</summary>
    Rejected,

    /// <summary>
    /// No se sabe si se emitió: un 10016, un error interno (500, 501, 502 «Transacción activa»), una falla SOAP o una
    /// respuesta que no se entiende. Hay que consultar con <c>FECompConsultar</c> antes de volver a pedir.
    /// </summary>
    Uncertain,
}

/// <summary>Un código y su texto, tal como los devuelve ARCA en <c>Errors</c> u <c>Observaciones</c>.</summary>
public sealed record ArcaMessage(int Code, string Text);

/// <summary>La respuesta de <c>FECAESolicitar</c> para un comprobante.</summary>
public sealed record ArcaAuthorization(ArcaAuthorizationResult Result, string? Cae, DateOnly? CaeDueOn, IReadOnlyList<ArcaMessage> Observations,
    IReadOnlyList<ArcaMessage> Errors);
