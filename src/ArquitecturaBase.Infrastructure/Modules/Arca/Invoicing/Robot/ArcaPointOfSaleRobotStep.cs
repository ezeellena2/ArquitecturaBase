using System.Globalization;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;
using Microsoft.Playwright;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing.Robot;

/// <summary>
/// El paso de Facturación del lado del cliente (ADR 0026): el punto de venta de web service en «Administración de puntos de
/// venta y domicilios». Si ya hay uno de web service vigente lo usa; si no, da de alta el siguiente número con el sistema
/// «… Web Services» que ARCA ofrezca para la empresa. Devuelve el número, que la pantalla deja elegido.
/// </summary>
internal sealed partial class ArcaPointOfSaleRobotStep : IArcaRobotCompanyStep
{
    public const string StepKey = "company.point-of-sale";
    private const string Wsfe = "wsfe";

    public string Key => StepKey;

    public bool AppliesTo(IReadOnlyCollection<string> enabledServices) => enabledServices.Contains(Wsfe, StringComparer.Ordinal);

    public async Task<string?> RunAsync(ArcaRobotSession session, ArcaCompanyRobotWorkResponse job, Func<string, Task<bool>> record,
        CancellationToken cancellationToken)
    {
        await session.OpenServiceAsync(ArcaRobotScreens.PointsOfSaleService, job.CompanyCuit);
        await session.ClickAsync(ArcaRobotScreens.PointsOfSaleAbm);
        // O9: la tabla se lee cuando la página cargó, con su «Agregar».
        await session.WaitForClickableAsync(ArcaRobotScreens.Add);

        // O10: un punto de venta propio: el que guardó una pasada anterior, si sigue vigente en la tabla; nunca el de otro sistema.
        if (job.Outputs.GetValueOrDefault(StepKey) is { } recorded
            && int.TryParse(recorded, NumberStyles.None, CultureInfo.InvariantCulture, out var own) && await Point(session, own).CountAsync() > 0)
            return recorded;

        var used = (await session.Rows(ArcaRobotScreens.Pattern(@"^\s*\d")).AllInnerTextsAsync()).Select(Number).OfType<int>().DefaultIfEmpty(0).Max();
        await session.ClickAsync(ArcaRobotScreens.Add);
        var number = int.TryParse(await session.ValueAsync(ArcaRobotScreens.PointNumberInput), NumberStyles.None, CultureInfo.InvariantCulture, out var offered)
            && offered > used ? offered : used + 1;
        await session.FillAsync(ArcaRobotScreens.PointNumberInput, number.ToString(CultureInfo.InvariantCulture));
        await session.SelectAsync(ArcaRobotScreens.SystemSelect, ArcaRobotScreens.Pattern(ArcaRobotScreens.WebServicesSystem));
        await session.SelectIfPresentAsync(ArcaRobotScreens.AddressSelect, ArcaRobotScreens.Pattern("\\S"));
        // O10: antes de confirmarlo en ARCA, la corrida guarda el número: si la pasada se corta, la próxima lo toma de la tabla.
        if (!await record(number.ToString(CultureInfo.InvariantCulture))) throw new ArcaRobotStoppedException(ArcaRobotFailure.Interrupted);
        await session.ClickAsync(ArcaRobotScreens.Accept);
        await session.ClickIfPresentAsync(ArcaRobotScreens.ConfirmPoint);

        // O9: espera la fila nueva después del clic.
        await session.WaitForAsync(Point(session, number));
        return number.ToString(CultureInfo.InvariantCulture);
    }

    // Una fila vigente de web service con ese número. Playwright compara con el texto de la fila sin separar las celdas
    // («0002Factura…»): el borde es un no dígito.
    private static ILocator Point(ArcaRobotSession session, int number) =>
        session.Rows(ArcaRobotScreens.Pattern(ArcaRobotScreens.WebServicesSystem))
            .Filter(new() { HasTextRegex = new Regex($@"(^|\D)0*{number}(\D|$)", RegexOptions.None, TimeSpan.FromSeconds(1)) })
            .Filter(new() { HasNotTextRegex = ArcaRobotScreens.Pattern(ArcaRobotScreens.DroppedPoint) });

    /// <summary>El número de punto de venta con que empieza una fila de la tabla.</summary>
    private static int? Number(string row) =>
        LeadingNumber().Match(row) is { Success: true } match && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number : null;

    [GeneratedRegex(@"^\s*0*(\d{1,5})\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LeadingNumber();
}
