namespace ArquitecturaBase.Api.Modules.Arca.Contracts.Invoicing;

/// <summary>
/// Los datos del comprobante que el padrón no trae, el número de ingresos brutos y el inicio de actividades, con la revisión
/// de la configuración de Facturación que se leyó (0 si no había). El número no se imprime.
/// </summary>
public sealed record SetArcaVoucherDataHttpRequest(string? GrossIncomeNumber, DateOnly ActivityStartOn, long ExpectedRevision)
{
    public override string ToString() =>
        $"SetArcaVoucherDataHttpRequest {{ grossIncomeNumber: [REDACTED], activityStartOn: {ActivityStartOn:yyyy-MM-dd}, expectedRevision: {ExpectedRevision} }}";
}
