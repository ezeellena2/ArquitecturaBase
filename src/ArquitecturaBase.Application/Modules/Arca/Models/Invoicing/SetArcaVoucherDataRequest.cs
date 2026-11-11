namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// Ingresos brutos e inicio de actividades, con la revisión de la configuración de Facturación que se leyó. El número de
/// ingresos brutos no se imprime.
/// </summary>
public sealed record SetArcaVoucherDataRequest(string? GrossIncomeNumber, DateOnly ActivityStartOn, long ExpectedRevision)
{
    public override string ToString() =>
        $"SetArcaVoucherDataRequest {{ grossIncomeNumber: [REDACTED], activityStartOn: {ActivityStartOn:yyyy-MM-dd}, expectedRevision: {ExpectedRevision} }}";
}
