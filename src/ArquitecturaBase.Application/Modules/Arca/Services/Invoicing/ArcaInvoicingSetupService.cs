using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Services.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>
/// El punto de venta de la plataforma y los datos del comprobante. El punto de venta se elige entre los que ARCA devuelve
/// en ese momento y, guardado, la conexión vuelve a calcular su estado con la configuración de todas las familias
/// elegidas. Ingresos brutos e inicio de actividades se guardan sin llamar a ARCA: sólo van en el PDF fiscal.
/// </summary>
internal sealed class ArcaInvoicingSetupService(ArcaServiceSessionIssuer access, IArcaInvoicingClient client, IArcaConnectionRepository connections,
    IArcaInvoicingRepository repository, IArcaInvoicingReader reader, IArcaConnectionTestService connectionTest, IUnitOfWork unitOfWork,
    ILogger<ArcaInvoicingSetupService> logger) : IArcaInvoicingSetupService
{
    private static readonly ArcaServiceCode Wsfe = ArcaServiceCode.Create("wsfe")!.Value;

    public Task<Result<ArcaInvoicingSettingsResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<ArcaInvoicingSettingsResponse>>(logger, "GetArcaInvoicingSettings", async () =>
            await access.FindConnectionIdAsync(cancellationToken) is { } connectionId
                ? Response(await reader.FindSettingsAsync(connectionId, cancellationToken))
                : ArcaConnectionErrors.NotConnected);

    public Task<Result<IReadOnlyList<ArcaPointOfSaleResponse>>> ListPointsOfSaleAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<IReadOnlyList<ArcaPointOfSaleResponse>>>(logger, "ListArcaPointsOfSale", async () =>
        {
            var points = await PointsOfSaleAsync(cancellationToken);
            if (points.IsFailure) return points.Error;
            return Result.Success<IReadOnlyList<ArcaPointOfSaleResponse>>([.. points.Value
                .Where(point => point.DroppedOn is null)
                .Select(point => new ArcaPointOfSaleResponse(point.Number, point.EmissionType, point.Blocked))]);
        });

    public Task<Result<ArcaInvoicingSettingsResponse>> ChoosePointOfSaleAsync(int number, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<ArcaInvoicingSettingsResponse>>(logger, "ChooseArcaPointOfSale", async () =>
        {
            var points = await PointsOfSaleAsync(cancellationToken);
            if (points.IsFailure) return points.Error;
            if (!points.Value.Any(point => point.Number == number && !point.Blocked && point.DroppedOn is null))
                return ArcaInvoicingErrors.PointOfSaleUnavailable;

            var saved = await unitOfWork.ExecuteInTransactionAsync(token => ChooseCoreAsync(number, token), CommitPolicy.OnSuccess, cancellationToken);
            if (saved.IsFailure) return saved.Error;
            var reevaluated = await connectionTest.ReevaluateAsync(cancellationToken);
            return reevaluated.IsFailure ? reevaluated.Error : saved.Value;
        });

    public Task<Result<ArcaInvoicingSettingsResponse>> SetVoucherDataAsync(string? grossIncomeNumber, DateOnly activityStartOn,
        CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "SetArcaVoucherData", () => unitOfWork.ExecuteInTransactionAsync(
            token => SetVoucherDataCoreAsync(grossIncomeNumber, activityStartOn, token), CommitPolicy.OnSuccess, cancellationToken));

    private async Task<Result<IReadOnlyList<ArcaPointOfSale>>> PointsOfSaleAsync(CancellationToken cancellationToken)
    {
        var session = await access.OpenAsync(Wsfe, cancellationToken);
        if (session.IsFailure) return session.Error;
        var reply = await client.ListPointsOfSaleAsync(session.Value.Certificate, session.Value.Ticket, cancellationToken);
        return reply is { Status: ArcaServiceCheckStatus.Ready, Value: { } points }
            ? Result.Success(points)
            : ArcaServiceSessionIssuer.Failure(reply.Status);
    }

    private async Task<Result<ArcaInvoicingSettingsResponse>> ChooseCoreAsync(int number, CancellationToken cancellationToken)
    {
        var settings = await LockSettingsAsync(cancellationToken);
        if (settings.IsFailure) return settings.Error;
        var chosen = settings.Value.ChoosePointOfSale(number);
        return chosen.IsFailure ? chosen.Error : Response(settings.Value);
    }

    private async Task<Result<ArcaInvoicingSettingsResponse>> SetVoucherDataCoreAsync(string? grossIncomeNumber, DateOnly activityStartOn,
        CancellationToken cancellationToken)
    {
        var settings = await LockSettingsAsync(cancellationToken);
        if (settings.IsFailure) return settings.Error;
        var set = settings.Value.SetVoucherData(grossIncomeNumber, activityStartOn);
        return set.IsFailure ? set.Error : Response(settings.Value);
    }

    /// <summary>La configuración de la conexión, creada si todavía no existe, con la conexión bloqueada.</summary>
    private async Task<Result<ArcaInvoicingSettings>> LockSettingsAsync(CancellationToken cancellationToken)
    {
        await connections.LockConnectionAsync(cancellationToken);
        if (await connections.GetConnectionAsync(cancellationToken) is not { } connection) return ArcaConnectionErrors.NotConnected;
        if (await repository.GetSettingsAsync(connection.Id, cancellationToken) is not { } settings)
        {
            settings = ArcaInvoicingSettings.Create(connection.Id);
            repository.Add(settings);
        }

        return settings;
    }

    private static ArcaInvoicingSettingsResponse Response(ArcaInvoicingSettings settings) =>
        new(settings.PointOfSale, settings.GrossIncomeNumber, settings.ActivityStartOn);

    private static ArcaInvoicingSettingsResponse Response(ArcaInvoicingSettingsRow? row) =>
        new(row?.PointOfSale, row?.GrossIncomeNumber, row?.ActivityStartOn);
}
