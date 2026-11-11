using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;
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
/// elegidas. Ingresos brutos e inicio de actividades se guardan sin llamar a ARCA: sólo van en el PDF fiscal. Los pedidos se
/// validan antes del límite, y se guardan sólo con la revisión que se leyó (ADR 0018).
/// </summary>
internal sealed class ArcaInvoicingSetupService(ArcaServiceSessionIssuer access, IArcaInvoicingClient client, ArcaInvoicingSettingsGuard settingsGuard,
    IArcaInvoicingReader reader, IArcaConnectionTestService connectionTest, IRequestValidator validator, IUnitOfWork unitOfWork,
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

    public Task<Result<ArcaInvoicingSettingsResponse>> ChoosePointOfSaleAsync(ChooseArcaPointOfSaleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result<ArcaInvoicingSettingsResponse>>(logger, "ChooseArcaPointOfSale", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var points = await PointsOfSaleAsync(cancellationToken);
            if (points.IsFailure) return points.Error;
            if (!points.Value.Any(point => point.Number == request.PointOfSale && !point.Blocked && point.DroppedOn is null))
                return ArcaInvoicingErrors.PointOfSaleUnavailable;

            var saved = await unitOfWork.ExecuteInTransactionAsync(token => ChooseCoreAsync(request, token), CommitPolicy.OnSuccess, cancellationToken);
            if (saved.IsFailure) return saved.Error;
            var reevaluated = await connectionTest.ReevaluateAsync(cancellationToken);
            return reevaluated.IsFailure ? reevaluated.Error : saved.Value;
        });
    }

    public Task<Result<ArcaInvoicingSettingsResponse>> SetVoucherDataAsync(SetArcaVoucherDataRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result<ArcaInvoicingSettingsResponse>>(logger, "SetArcaVoucherData", async () =>
            await validator.ValidateAsync(request, cancellationToken) is { } invalid
                ? invalid
                : await unitOfWork.ExecuteInTransactionAsync(token => SetVoucherDataCoreAsync(request, token), CommitPolicy.OnSuccess, cancellationToken));
    }

    private async Task<Result<IReadOnlyList<ArcaPointOfSale>>> PointsOfSaleAsync(CancellationToken cancellationToken)
    {
        var session = await access.OpenAsync(Wsfe, cancellationToken);
        if (session.IsFailure) return session.Error;
        var reply = await client.ListPointsOfSaleAsync(session.Value.Certificate, session.Value.Ticket, cancellationToken);
        return reply is { Status: ArcaServiceCheckStatus.Ready, Value: { } points }
            ? Result.Success(points)
            : ArcaServiceSessionIssuer.Failure(reply.Status);
    }

    private async Task<Result<ArcaInvoicingSettingsResponse>> ChooseCoreAsync(ChooseArcaPointOfSaleRequest request, CancellationToken cancellationToken)
    {
        var settings = await settingsGuard.LockAsync(request.ExpectedRevision, cancellationToken);
        if (settings.IsFailure) return settings.Error;
        var chosen = settings.Value.ChoosePointOfSale(request.PointOfSale);
        return chosen.IsFailure ? chosen.Error : Response(settings.Value);
    }

    private async Task<Result<ArcaInvoicingSettingsResponse>> SetVoucherDataCoreAsync(SetArcaVoucherDataRequest request, CancellationToken cancellationToken)
    {
        var settings = await settingsGuard.LockAsync(request.ExpectedRevision, cancellationToken);
        if (settings.IsFailure) return settings.Error;
        var set = settings.Value.SetVoucherData(request.GrossIncomeNumber, request.ActivityStartOn);
        return set.IsFailure ? set.Error : Response(settings.Value);
    }

    private static ArcaInvoicingSettingsResponse Response(ArcaInvoicingSettings settings) =>
        new(settings.PointOfSale, settings.GrossIncomeNumber, settings.ActivityStartOn, settings.Revision);

    private static ArcaInvoicingSettingsResponse Response(ArcaInvoicingSettingsRow? row) =>
        new(row?.PointOfSale, row?.GrossIncomeNumber, row?.ActivityStartOn, row?.Revision ?? 0);
}
