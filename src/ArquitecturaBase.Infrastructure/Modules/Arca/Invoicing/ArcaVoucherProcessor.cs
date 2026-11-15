using System.Security.Cryptography;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;

/// <summary>
/// Procesa una secuencia de la cola como el proveedor de tickets: cada paso guardado en un scope propio, con su DbContext y su
/// transacción, y los pedidos a ARCA entre uno y otro, sin transacción (ADR 0028). Sin número: último autorizado, guardar el
/// número y la huella, pedir el CAE. Con número: reconciliar con <c>FECompConsultar</c> antes de volver a pedir. Si un paso
/// encuentra la reserva tomada por otro host, se detiene sin guardar: el otro reconcilia.
/// </summary>
internal sealed partial class ArcaVoucherProcessor(IServiceScopeFactory scopes, IArcaAccessTicketProvider tickets, IArcaInvoicingClient client,
    IArcaFiscalPdfRenderer renderer, IPrivateFileStore files, IOptions<ArcaVoucherWorkerOptions> options, TimeProvider timeProvider,
    ILogger<ArcaVoucherProcessor> logger)
{
    private static readonly ArcaServiceCode Wsfe = ArcaServiceCode.Create("wsfe")!.Value;

    /// <summary>
    /// Las secuencias con trabajo vencido, de a una; una falla de una no frena a las demás. Dice si alguna pidió un CAE: una
    /// secuencia frenada, una espera o una falla no cuentan (H33).
    /// </summary>
    public async Task<bool> ProcessDueAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ArcaVoucherSequenceRow> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<IArcaVoucherReader>()
                .ListDueSequencesAsync(timeProvider.GetUtcNow().UtcDateTime, options.Value.BatchSize, cancellationToken);
        }

        var askedForCae = false;
        foreach (var sequence in due)
        {
            try
            {
                askedForCae |= await ProcessAsync(sequence, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogFailed(exception.GetType().Name, sequence.PointOfSale, sequence.VoucherType);
            }
        }

        return askedForCae;
    }

    /// <summary>Los PDF fiscales que faltan, de a uno; uno que falla no frena a los demás (H17). Dice si guardó alguno (H33).</summary>
    public async Task<bool> ProcessDuePdfsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<IArcaVoucherReader>()
                .ListDuePdfsAsync(timeProvider.GetUtcNow().UtcDateTime, options.Value.BatchSize, cancellationToken);
        }

        var stored = false;
        foreach (var voucherId in due)
        {
            try
            {
                stored |= await ProcessPdfAsync(voucherId, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogPdfFailed(exception.GetType().Name);
            }
        }

        return stored;
    }

    /// <summary>
    /// Dibuja el PDF fiscal, lo escribe una sola vez (si otro host lo escribió antes, queda el suyo), lo relee para confirmar
    /// su SHA-256 y guarda la referencia. Si no se puede armar o el almacén falla, el comprobante sigue autorizado y el PDF espera,
    /// cada uno con su código (H17). Dice si lo guardó.
    /// </summary>
    public async Task<bool> ProcessPdfAsync(Guid voucherId, CancellationToken cancellationToken)
    {
        ArcaFiscalPdfWorkResponse? work;
        byte[] pdf;
        try
        {
            work = await StepAsync(store => store.FindPdfAsync(voucherId, cancellationToken));
            if (work is null) return false;
            pdf = renderer.Render(work.Document);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogPdfFailed(exception.GetType().Name);
            await StepAsync(store => store.PostponePdfAsync(voucherId, ArcaInvoicingErrors.PdfRenderFailed.Code, cancellationToken));
            return false;
        }

        try
        {
            if (!files.IsAvailable) throw new InvalidOperationException("The private file store is not configured.");
            await using (var content = new MemoryStream(pdf))
            {
                await files.WriteOnceAsync(work.StorageKey, content, "application/pdf", cancellationToken);
            }

            await using var stored = await files.OpenReadAsync(work.StorageKey, cancellationToken)
                ?? throw new InvalidOperationException("The fiscal PDF is missing right after writing it.");
            using var buffer = new MemoryStream();
            await stored.CopyToAsync(buffer, cancellationToken);
            var sha = Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
            return await StepAsync(store => store.AttachPdfAsync(voucherId, work.StorageKey, sha, buffer.Length, cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogPdfFailed(exception.GetType().Name);
            await StepAsync(store => store.PostponePdfAsync(voucherId, ArcaInvoicingErrors.PdfStorageUnavailable.Code, cancellationToken));
            return false;
        }
    }

    /// <summary>Una vuelta de la secuencia; dice si pidió un CAE (H33).</summary>
    public async Task<bool> ProcessAsync(ArcaVoucherSequenceRow sequence, CancellationToken cancellationToken)
    {
        var work = await StepAsync(store => store.ReserveAsync(sequence, Guid.NewGuid(), options.Value.Lease, cancellationToken));
        if (work is null) return false;

        var ticket = await tickets.GetAsync(work.Certificate, Wsfe, cancellationToken);
        if (ticket is not ArcaTicketOutcome.Ready ready)
        {
            await StepAsync(store => store.PostponeAsync(work, TicketFailure(ticket), cancellationToken));
            return false;
        }

        var toSend = work.HasNumber ? await ReconcileAsync(work, ready, cancellationToken) : await NumberAsync(work, ready, cancellationToken);
        if (toSend is null) return false;

        var reply = await client.RequestAuthorizationAsync(toSend.Certificate, ready, toSend.Request, cancellationToken);
        if (reply is { Status: ArcaServiceCheckStatus.Ready, Value: { } authorization })
        {
            await StepAsync(store => store.CompleteAsync(toSend, authorization, cancellationToken));
            LogAnswered(authorization.Result, toSend.Request.PointOfSale, toSend.Request.VoucherType, toSend.Request.Number);
        }
        else
        {
            await StepAsync(store => store.PostponeAsync(toSend, ArcaInvoicingErrors.ArcaNoAnswer.Code, cancellationToken));
            LogUnanswered(toSend.Request.PointOfSale, toSend.Request.VoucherType, toSend.Request.Number);
        }

        return true;
    }

    /// <summary>El último autorizado más 1, guardado con su huella antes de pedir; sin respuesta, espera sin número.</summary>
    private async Task<ArcaVoucherWorkResponse?> NumberAsync(ArcaVoucherWorkResponse work, ArcaTicketOutcome.Ready ticket, CancellationToken cancellationToken)
    {
        var last = await client.GetLastAuthorizedAsync(work.Certificate, ticket, work.Request.PointOfSale, work.Request.VoucherType, cancellationToken);
        if (last is not { Status: ArcaServiceCheckStatus.Ready })
        {
            await StepAsync(store => store.PostponeAsync(work, Failure(last.Status, last.FailureCode), cancellationToken));
            return null;
        }

        return await StepAsync(store => store.AssignNumberAsync(work, last.Value, cancellationToken));
    }

    /// <summary>
    /// Lo que ARCA tiene con ese número y, si no lo tiene, el último autorizado y, si el número sigue siendo el siguiente, la
    /// fecha de ese último, que dice si ARCA puede aceptar la nuestra (ADR 0031); sin respuesta, sigue incierto.
    /// </summary>
    private async Task<ArcaVoucherWorkResponse?> ReconcileAsync(ArcaVoucherWorkResponse work, ArcaTicketOutcome.Ready ticket,
        CancellationToken cancellationToken)
    {
        var request = work.Request;
        var found = await client.FindVoucherAsync(work.Certificate, ticket, request.PointOfSale, request.VoucherType, request.Number, cancellationToken);
        if (found.Status != ArcaServiceCheckStatus.Ready)
        {
            await StepAsync(store => store.PostponeAsync(work, Failure(found.Status, found.FailureCode), cancellationToken));
            return null;
        }

        var lastAuthorized = 0L;
        DateOnly? lastIssuedOn = null;
        if (found.Value is null)
        {
            var last = await client.GetLastAuthorizedAsync(work.Certificate, ticket, request.PointOfSale, request.VoucherType, cancellationToken);
            if (last.Status != ArcaServiceCheckStatus.Ready)
            {
                await StepAsync(store => store.PostponeAsync(work, Failure(last.Status, last.FailureCode), cancellationToken));
                return null;
            }

            lastAuthorized = last.Value;
            if (lastAuthorized > 0 && lastAuthorized < request.Number)
            {
                var latest = await client.FindVoucherAsync(work.Certificate, ticket, request.PointOfSale, request.VoucherType, lastAuthorized, cancellationToken);
                if (latest.Status != ArcaServiceCheckStatus.Ready)
                {
                    await StepAsync(store => store.PostponeAsync(work, Failure(latest.Status, latest.FailureCode), cancellationToken));
                    return null;
                }

                lastIssuedOn = latest.Value?.IssuedOn;
            }
        }

        LogReconciled(request.PointOfSale, request.VoucherType, request.Number, found.Value is not null);
        return await StepAsync(store => store.ReconcileAsync(work, found.Value, lastAuthorized, lastIssuedOn, cancellationToken));
    }

    private static string TicketFailure(ArcaTicketOutcome outcome) => outcome switch
    {
        ArcaTicketOutcome.Blocked => "Arca.Connection.ServiceBlocked",
        ArcaTicketOutcome.NotAuthorized => "Arca.Connection.ServiceNotAuthorized",
        ArcaTicketOutcome.NotDelegated => "Arca.Connection.ServiceNotDelegated",
        ArcaTicketOutcome.ClockSkew => "Arca.Connection.ClockSkew",
        _ => "Arca.Connection.Unavailable",
    };

    private static string Failure(ArcaServiceCheckStatus status, string? code) => status switch
    {
        ArcaServiceCheckStatus.NotAuthorized => "Arca.Connection.ServiceNotAuthorized",
        ArcaServiceCheckStatus.Unavailable => ArcaInvoicingErrors.ArcaNoAnswer.Code,
        _ => code ?? ArcaInvoicingErrors.ArcaFailed.Code,
    };

    private async Task<T> StepAsync<T>(Func<IArcaVoucherStore, Task<T>> step)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await step(scope.ServiceProvider.GetRequiredService<IArcaVoucherStore>());
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "ARCA answered {Result} for voucher type {VoucherType} number {Number} of point of sale {PointOfSale}.")]
    private partial void LogAnswered(ArcaAuthorizationResult result, int pointOfSale, int voucherType, long number);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ARCA did not answer for voucher type {VoucherType} number {Number} of point of sale {PointOfSale}; it is reconciled next time.")]
    private partial void LogUnanswered(int pointOfSale, int voucherType, long number);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconciled voucher type {VoucherType} number {Number} of point of sale {PointOfSale}; found in ARCA: {Found}.")]
    private partial void LogReconciled(int pointOfSale, int voucherType, long number, bool found);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A fiscal PDF could not be stored after {ExceptionType}; the voucher stays authorized and the PDF waits.")]
    private partial void LogPdfFailed(string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ARCA voucher sequence of type {VoucherType} at point of sale {PointOfSale} failed with {ExceptionType}; it is retried later.")]
    private partial void LogFailed(string exceptionType, int pointOfSale, int voucherType);
}
