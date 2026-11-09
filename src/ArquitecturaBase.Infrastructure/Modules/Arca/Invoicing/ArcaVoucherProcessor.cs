using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
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
    IOptions<ArcaVoucherWorkerOptions> options, TimeProvider timeProvider, ILogger<ArcaVoucherProcessor> logger)
{
    private static readonly ArcaServiceCode Wsfe = ArcaServiceCode.Create("wsfe")!.Value;

    /// <summary>Las secuencias con trabajo vencido, de a una; una falla de una no frena a las demás.</summary>
    public async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ArcaVoucherSequenceRow> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<IArcaVoucherReader>()
                .ListDueSequencesAsync(timeProvider.GetUtcNow().UtcDateTime, options.Value.BatchSize, cancellationToken);
        }

        foreach (var sequence in due)
        {
            try
            {
                await ProcessAsync(sequence, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogFailed(exception.GetType().Name, sequence.PointOfSale, sequence.VoucherType);
            }
        }
    }

    public async Task ProcessAsync(ArcaVoucherSequenceRow sequence, CancellationToken cancellationToken)
    {
        var work = await StepAsync(store => store.ReserveAsync(sequence, Guid.NewGuid(), options.Value.Lease, cancellationToken));
        if (work is null) return;

        var ticket = await tickets.GetAsync(work.Certificate, Wsfe, cancellationToken);
        if (ticket is not ArcaTicketOutcome.Ready ready)
        {
            await StepAsync(store => store.PostponeAsync(work, TicketFailure(ticket), cancellationToken));
            return;
        }

        var toSend = work.HasNumber ? await ReconcileAsync(work, ready, cancellationToken) : await NumberAsync(work, ready, cancellationToken);
        if (toSend is null) return;

        var reply = await client.RequestAuthorizationAsync(toSend.Certificate, ready, toSend.Request, cancellationToken);
        if (reply is { Status: ArcaServiceCheckStatus.Ready, Value: { } authorization })
        {
            await StepAsync(store => store.CompleteAsync(toSend, authorization, cancellationToken));
            LogAnswered(authorization.Result, toSend.Request.PointOfSale, toSend.Request.VoucherType, toSend.Request.Number);
        }
        else
        {
            await StepAsync(store => store.PostponeAsync(toSend, "wsfe.unavailable", cancellationToken));
            LogUnanswered(toSend.Request.PointOfSale, toSend.Request.VoucherType, toSend.Request.Number);
        }
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

    /// <summary>Lo que ARCA tiene con ese número y, si no lo tiene, el último autorizado; sin respuesta, sigue incierto.</summary>
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
        if (found.Value is null)
        {
            var last = await client.GetLastAuthorizedAsync(work.Certificate, ticket, request.PointOfSale, request.VoucherType, cancellationToken);
            if (last.Status != ArcaServiceCheckStatus.Ready)
            {
                await StepAsync(store => store.PostponeAsync(work, Failure(last.Status, last.FailureCode), cancellationToken));
                return null;
            }

            lastAuthorized = last.Value;
        }

        LogReconciled(request.PointOfSale, request.VoucherType, request.Number, found.Value is not null);
        return await StepAsync(store => store.ReconcileAsync(work, found.Value, lastAuthorized, cancellationToken));
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
        ArcaServiceCheckStatus.Unavailable => "wsfe.unavailable",
        _ => code ?? "wsfe.failed",
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ARCA voucher sequence of type {VoucherType} at point of sale {PointOfSale} failed with {ExceptionType}; it is retried later.")]
    private partial void LogFailed(string exceptionType, int pointOfSale, int voucherType);
}
