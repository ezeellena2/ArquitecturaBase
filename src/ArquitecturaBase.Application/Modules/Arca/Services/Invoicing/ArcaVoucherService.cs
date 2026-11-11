using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Channels.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>
/// Encolar valida y recalcula antes de la transacción y, adentro, con el lock de la conexión, decide la clase con los datos
/// del padrón, calcula los importes de esa clase (el IVA en la A y la B), fija lo que va a emitir y guarda (ADR 0028). La
/// misma clave de pedido devuelve la emisión que ya estaba. Nada llama a ARCA: lo hace el worker. La vista previa decide la
/// clase, el total y el IVA sin guardar nada.
/// </summary>
internal sealed class ArcaVoucherService(IArcaConnectionRepository connections, IArcaInvoicingRepository settings, IArcaVoucherRepository vouchers,
    IArcaCompanyProfileSource profiles, IUnitOfWork unitOfWork, TimeProvider timeProvider, ILogger<ArcaVoucherService> logger) : IArcaVoucherService
{
    public Task<Result<ArcaVoucherResponse>> EnqueueAsync(ArcaVoucherOrder order, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "EnqueueArcaVoucher", async () =>
        {
            var lines = ArcaVoucherOrderPolicy.Lines(order, Today());
            if (lines.IsFailure) return Result.Failure<ArcaVoucherResponse>(lines.Error);
            return await unitOfWork.ExecuteInTransactionAsync(token => EnqueueCoreAsync(order, lines.Value, token), CommitPolicy.OnSuccess, cancellationToken);
        });

    public Task<Result<ArcaVoucherResponse>> EnqueueCreditNoteAsync(Guid requestKey, Guid invoiceRequestKey, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "EnqueueArcaCreditNote", () =>
            unitOfWork.ExecuteInTransactionAsync(token => CreditNoteCoreAsync(requestKey, invoiceRequestKey, token), CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result<ArcaVoucherPreviewResponse>> PreviewAsync(ArcaVoucherOrder order, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "PreviewArcaVoucher", async () =>
        {
            var lines = ArcaVoucherOrderPolicy.Lines(order, Today());
            if (lines.IsFailure) return Result.Failure<ArcaVoucherPreviewResponse>(lines.Error);
            // Sólo lee, sin transacción: no guarda nada.
            if (await connections.GetConnectionAsync(cancellationToken) is not { } connection) return ArcaConnectionErrors.NotConnected;
            if ((await settings.GetSettingsAsync(connection.Id, cancellationToken))?.PointOfSale is null) return ArcaInvoicingErrors.PointOfSaleMissing;
            if (await profiles.FindAsync(connection.Id, cancellationToken) is not { } profile) return ArcaInvoicingErrors.IssuerDataMissing;
            var decided = ArcaVoucherClassPolicy.Decide(profile.TaxStatus, order.Receiver.VatConditionId);
            if (decided.IsFailure) return decided.Error;
            var priced = ArcaVoucherOrderPolicy.ForClass(decided.Value, order, lines.Value);
            if (priced.IsFailure) return priced.Error;
            return new ArcaVoucherPreviewResponse(decided.Value, ArcaVoucherTypes.Code(decided.Value, ArcaVoucherKind.Invoice), priced.Value.Amounts.Total,
                priced.Value.Amounts.Vat) { Amounts = priced.Value.Amounts, CompanyTaxStatus = profile.TaxStatus };
        });

    private async Task<Result<ArcaVoucherResponse>> EnqueueCoreAsync(ArcaVoucherOrder order, IReadOnlyList<ArcaVoucherLine> lines, CancellationToken cancellationToken)
    {
        await connections.LockConnectionAsync(cancellationToken);
        if (await vouchers.GetVoucherByRequestAsync(order.RequestKey, cancellationToken) is { } existing) return ArcaVoucherRowPolicy.Response(existing);
        if (await connections.GetConnectionAsync(cancellationToken) is not { } connection) return ArcaConnectionErrors.NotConnected;
        if (await settings.GetSettingsAsync(connection.Id, cancellationToken) is not { PointOfSale: { } pointOfSale } configuration)
            return ArcaInvoicingErrors.PointOfSaleMissing;
        if (await profiles.FindAsync(connection.Id, cancellationToken) is not { } profile) return ArcaInvoicingErrors.IssuerDataMissing;
        var decided = ArcaVoucherClassPolicy.Decide(profile.TaxStatus, order.Receiver.VatConditionId);
        if (decided.IsFailure) return decided.Error;
        var priced = ArcaVoucherOrderPolicy.ForClass(decided.Value, order, lines);
        if (priced.IsFailure) return priced.Error;

        var receiver = order.Receiver with { Name = order.Receiver.Name.Trim(), Address = Trimmed(order.Receiver.Address) };
        var payload = new ArcaVoucherPayload(receiver, order.ServiceFrom, order.ServiceTo, order.PaymentDueOn, priced.Value.Lines,
            Issuer(profile, configuration.GrossIncomeNumber, configuration.ActivityStartOn), null,
            decided.Value != ArcaVoucherClass.C && order.PricesIncludeVat);
        var voucher = ArcaVoucher.Create(connection.Id, connection.Environment, order.RequestKey, decided.Value, ArcaVoucherKind.Invoice, connection.Cuit,
            pointOfSale, order.Concept, receiver.DocumentType, receiver.DocumentNumber, order.IssueOn, priced.Value.Amounts.Total,
            ArcaVoucherRequestPolicy.ToJson(payload), null, timeProvider.GetUtcNow().UtcDateTime);
        vouchers.Add(voucher);
        return ArcaVoucherRowPolicy.Response(voucher);
    }

    private async Task<Result<ArcaVoucherResponse>> CreditNoteCoreAsync(Guid requestKey, Guid invoiceRequestKey, CancellationToken cancellationToken)
    {
        await connections.LockConnectionAsync(cancellationToken);
        if (await vouchers.GetVoucherByRequestAsync(requestKey, cancellationToken) is { } existing) return ArcaVoucherRowPolicy.Response(existing);
        if (await vouchers.GetVoucherByRequestAsync(invoiceRequestKey, cancellationToken) is not { } invoice) return ArcaInvoicingErrors.VoucherNotFound;
        if (invoice is not { Kind: ArcaVoucherKind.Invoice, State: ArcaVoucherState.Authorized, Number: { } number }) return ArcaInvoicingErrors.InvoiceNotAuthorized;
        if (await vouchers.ExistsCreditNoteAsync(invoice.Id, cancellationToken)) return ArcaInvoicingErrors.InvoiceAlreadyCancelled;

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var today = ArcaIssueDateWindow.Today(nowUtc);
        var original = ArcaVoucherRequestPolicy.FromJson(invoice.PayloadJson);
        // El vencimiento del pago de un servicio no puede ser anterior a la fecha de la nota.
        var payload = original with
        {
            PaymentDueOn = original.PaymentDueOn is { } due && due < today ? today : original.PaymentDueOn,
            Cancels = new ArcaAssociatedVoucher(invoice.VoucherType, invoice.PointOfSale, number, invoice.Cuit, invoice.IssuedOn),
        };
        var note = ArcaVoucher.Create(invoice.ConnectionId, invoice.Environment, requestKey, invoice.Class, ArcaVoucherKind.CreditNote, invoice.Cuit,
            invoice.PointOfSale, invoice.Concept, invoice.DocumentType, invoice.DocumentNumber, today, invoice.Total, ArcaVoucherRequestPolicy.ToJson(payload),
            invoice.Id, nowUtc);
        vouchers.Add(note);
        return ArcaVoucherRowPolicy.Response(note);
    }

    private static ArcaVoucherIssuer Issuer(ArcaCompanyProfile profile, string? grossIncomeNumber, DateOnly? activityStartOn) =>
        new(profile.LegalName, profile.FiscalAddress, profile.TaxStatus, grossIncomeNumber, activityStartOn);

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private DateOnly Today() => ArcaIssueDateWindow.Today(timeProvider.GetUtcNow().UtcDateTime);
}
