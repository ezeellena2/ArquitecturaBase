using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>
/// Cada paso de una emisión en su transacción corta, con el lock de la conexión (ADR 0028). Reservar toma la secuencia con un
/// fence nuevo; los demás pasos exigen esa reserva y la sueltan al terminar, salvo el que deja el comprobante listo para
/// mandarse (con su número guardado o para reenviarlo con el mismo).
/// </summary>
internal sealed class ArcaVoucherStore(IArcaConnectionRepository connections, IArcaConnectionReader connectionReader, IArcaVoucherRepository vouchers,
    IUnitOfWork unitOfWork, TimeProvider timeProvider) : IArcaVoucherStore
{
    private const string Wsfe = "wsfe";

    public async Task<ArcaVoucherWorkResponse?> ReserveAsync(ArcaVoucherSequenceRow sequence, Guid owner, TimeSpan lease, CancellationToken cancellationToken) =>
        (await unitOfWork.ExecuteInTransactionAsync(token => ReserveCoreAsync(sequence, owner, lease, token), CommitPolicy.OnSuccess, cancellationToken)).Value;

    public async Task<ArcaVoucherWorkResponse?> AssignNumberAsync(ArcaVoucherWorkResponse work, long lastAuthorized, CancellationToken cancellationToken) =>
        (await unitOfWork.ExecuteInTransactionAsync(token => AssignNumberCoreAsync(work, lastAuthorized, token), CommitPolicy.OnSuccess, cancellationToken)).Value;

    public async Task<ArcaVoucherWorkResponse?> ReconcileAsync(ArcaVoucherWorkResponse work, ArcaIssuedVoucher? found, long lastAuthorized,
        CancellationToken cancellationToken) =>
        (await unitOfWork.ExecuteInTransactionAsync(token => ReconcileCoreAsync(work, found, lastAuthorized, token), CommitPolicy.OnSuccess, cancellationToken)).Value;

    public async Task<bool> CompleteAsync(ArcaVoucherWorkResponse work, ArcaAuthorization authorization, CancellationToken cancellationToken) =>
        (await unitOfWork.ExecuteInTransactionAsync(token => ChangeCoreAsync(work, (voucher, sequence, nowUtc) => Complete(voucher, sequence, authorization, nowUtc), token),
            CommitPolicy.OnSuccess, cancellationToken)).Value;

    public async Task<bool> PostponeAsync(ArcaVoucherWorkResponse work, string failureCode, CancellationToken cancellationToken) =>
        (await unitOfWork.ExecuteInTransactionAsync(token => ChangeCoreAsync(work, (voucher, _, nowUtc) => Wait(voucher, failureCode, nowUtc), token),
            CommitPolicy.OnSuccess, cancellationToken)).Value;

    public async Task<ArcaFiscalPdfWorkResponse?> FindPdfAsync(Guid voucherId, CancellationToken cancellationToken) =>
        await vouchers.GetVoucherAsync(voucherId, cancellationToken) is { State: ArcaVoucherState.Authorized, PdfStorageKey: null } voucher
            ? new ArcaFiscalPdfWorkResponse(voucher.Id, ArcaFiscalDocumentPolicy.StorageKey(voucher.Id),
                ArcaFiscalDocumentPolicy.Build(voucher, ArcaVoucherRequestPolicy.FromJson(voucher.PayloadJson)))
            : null;

    public async Task<bool> AttachPdfAsync(Guid voucherId, string storageKey, string sha256, long size, CancellationToken cancellationToken) =>
        (await unitOfWork.ExecuteInTransactionAsync(token => PdfCoreAsync(voucherId, voucher => voucher.AttachPdf(storageKey, sha256, size), token),
            CommitPolicy.OnSuccess, cancellationToken)).Value;

    public async Task<bool> PostponePdfAsync(Guid voucherId, string failureCode, CancellationToken cancellationToken) =>
        (await unitOfWork.ExecuteInTransactionAsync(token => PdfCoreAsync(voucherId,
            voucher => voucher.PostponePdf(failureCode, timeProvider.GetUtcNow().UtcDateTime), token), CommitPolicy.OnSuccess, cancellationToken)).Value;

    private async Task<Result<bool>> PdfCoreAsync(Guid voucherId, Action<ArcaVoucher> change, CancellationToken cancellationToken)
    {
        await connections.LockConnectionAsync(cancellationToken);
        if (await vouchers.GetVoucherAsync(voucherId, cancellationToken) is not { State: ArcaVoucherState.Authorized, PdfStorageKey: null } voucher) return false;
        change(voucher);
        return true;
    }

    private async Task<Result<ArcaVoucherWorkResponse?>> ReserveCoreAsync(ArcaVoucherSequenceRow key, Guid owner, TimeSpan lease, CancellationToken cancellationToken)
    {
        await connections.LockConnectionAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var sequence = await vouchers.GetSequenceAsync(key, cancellationToken);
        if (sequence is null)
        {
            sequence = ArcaVoucherSequence.Create(key.Environment, key.Cuit, key.PointOfSale, key.VoucherType);
            vouchers.Add(sequence);
        }

        if (sequence.TryReserve(owner, nowUtc, lease) is not { } fence) return Result.Success<ArcaVoucherWorkResponse?>(null);
        var voucher = await vouchers.GetNextAsync(sequence, cancellationToken);
        // Un comprobante «enviando» con la secuencia libre quedó de un host que se cayó: se reconcilia.
        if (voucher is null || voucher.State == ArcaVoucherState.NeedsReview || (voucher.State != ArcaVoucherState.Sending && !voucher.IsDue(nowUtc)))
            return Nothing(sequence, owner, fence);

        var certificate = await connectionReader.FindSigningCertificateAsync(ArcaCertificateRole.Active, cancellationToken);
        if (certificate is null || certificate.Environment != voucher.Environment || certificate.Cuit != voucher.Cuit)
        {
            Wait(voucher, ArcaConnectionErrors.NotConnected.Code, nowUtc);
            return Nothing(sequence, owner, fence);
        }

        if (voucher.Number is null && !ArcaIssueDateWindow.Admits(voucher.Concept, voucher.RequestedOn, ArcaIssueDateWindow.Today(nowUtc)))
        {
            voucher.RequireReview(ArcaVoucherReviewReason.DateOutOfRange, nowUtc);
            return Nothing(sequence, owner, fence);
        }

        var request = ArcaVoucherRequestPolicy.Build(voucher, ArcaVoucherRequestPolicy.FromJson(voucher.PayloadJson), voucher.Number ?? 0);
        return Result.Success<ArcaVoucherWorkResponse?>(new ArcaVoucherWorkResponse(voucher.Id, owner, fence, key,
            certificate.ToRef(new HashSet<string>(StringComparer.Ordinal) { Wsfe }), request, voucher.Number is not null));
    }

    private async Task<Result<ArcaVoucherWorkResponse?>> AssignNumberCoreAsync(ArcaVoucherWorkResponse work, long lastAuthorized, CancellationToken cancellationToken)
    {
        if (await OwnedAsync(work, cancellationToken) is not (var voucher, var sequence, var nowUtc)) return Result.Success<ArcaVoucherWorkResponse?>(null);
        // Una persona la canceló mientras tanto: no toma número.
        if (voucher.State != ArcaVoucherState.Queued || voucher.Number is not null)
        {
            sequence.Release(work.Owner, work.Fence);
            return Result.Success<ArcaVoucherWorkResponse?>(null);
        }

        var request = work.Request with { Number = lastAuthorized + 1 };
        voucher.AssignNumber(request.Number, ArcaVoucherRequestPolicy.Fingerprint(request), nowUtc);
        sequence.Hold(voucher.Id);
        return Result.Success<ArcaVoucherWorkResponse?>(work with { Request = request, HasNumber = true });
    }

    private async Task<Result<ArcaVoucherWorkResponse?>> ReconcileCoreAsync(ArcaVoucherWorkResponse work, ArcaIssuedVoucher? found, long lastAuthorized,
        CancellationToken cancellationToken)
    {
        if (await OwnedAsync(work, cancellationToken) is not (var voucher, var sequence, var nowUtc)) return Result.Success<ArcaVoucherWorkResponse?>(null);
        switch (ArcaReconciliationPolicy.Decide(work.Request, found, lastAuthorized))
        {
            case ArcaReconciliation.NotIssued:
                voucher.Resend(nowUtc);
                return Result.Success<ArcaVoucherWorkResponse?>(work);
            case ArcaReconciliation.Issued when found!.AuthorizationDueOn is { } due:
                voucher.Authorize(found.AuthorizationCode!, due, null, nowUtc);
                sequence.ReleasePending(voucher.Id);
                break;
            case ArcaReconciliation.Issued:
                voucher.MarkUncertain("wsfe.incomplete", nowUtc);
                break;
            case ArcaReconciliation.Taken:
                voucher.RequireReview(ArcaVoucherReviewReason.NumberTaken, nowUtc);
                break;
            default:
                voucher.RequireReview(ArcaVoucherReviewReason.NumberMissing, nowUtc);
                break;
        }

        sequence.Release(work.Owner, work.Fence);
        return Result.Success<ArcaVoucherWorkResponse?>(null);
    }

    private async Task<Result<bool>> ChangeCoreAsync(ArcaVoucherWorkResponse work, Action<ArcaVoucher, ArcaVoucherSequence, DateTime> change,
        CancellationToken cancellationToken)
    {
        if (await OwnedAsync(work, cancellationToken) is not (var voucher, var sequence, var nowUtc)) return false;
        change(voucher, sequence, nowUtc);
        sequence.Release(work.Owner, work.Fence);
        return true;
    }

    private static void Complete(ArcaVoucher voucher, ArcaVoucherSequence sequence, ArcaAuthorization authorization, DateTime nowUtc)
    {
        var messages = ArcaVoucherRowPolicy.Messages(authorization);
        switch (authorization)
        {
            case { Result: ArcaAuthorizationResult.Approved, Cae: { } cae, CaeDueOn: { } due }:
                voucher.Authorize(cae, due, messages, nowUtc);
                sequence.ReleasePending(voucher.Id);
                break;
            case { Result: ArcaAuthorizationResult.Rejected }:
                voucher.Reject(ArcaVoucherRowPolicy.FailureCode(authorization, "wsfe.rejected"), messages, nowUtc);
                sequence.ReleasePending(voucher.Id);
                break;
            default:
                voucher.MarkUncertain(ArcaVoucherRowPolicy.FailureCode(authorization, "wsfe.uncertain"), nowUtc);
                break;
        }
    }

    /// <summary>Con número, no se sabe si ARCA lo usó: queda incierto. Sin número, sólo espera.</summary>
    private static void Wait(ArcaVoucher voucher, string failureCode, DateTime nowUtc)
    {
        if (voucher.Number is not null) voucher.MarkUncertain(failureCode, nowUtc);
        else voucher.Postpone(failureCode, nowUtc);
    }

    private static Result<ArcaVoucherWorkResponse?> Nothing(ArcaVoucherSequence sequence, Guid owner, long fence)
    {
        sequence.Release(owner, fence);
        return Result.Success<ArcaVoucherWorkResponse?>(null);
    }

    private async Task<(ArcaVoucher Voucher, ArcaVoucherSequence Sequence, DateTime NowUtc)?> OwnedAsync(ArcaVoucherWorkResponse work, CancellationToken cancellationToken)
    {
        await connections.LockConnectionAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (await vouchers.GetSequenceAsync(work.Sequence, cancellationToken) is not { } sequence || !sequence.Owns(work.Owner, work.Fence, nowUtc)) return null;
        return await vouchers.GetVoucherAsync(work.VoucherId, cancellationToken) is { } voucher ? (voucher, sequence, nowUtc) : null;
    }
}
