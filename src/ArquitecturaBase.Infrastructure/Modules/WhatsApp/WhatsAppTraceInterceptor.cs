using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>H19: a new message or send keeps the trace context where it is saved, which its worker continues.</summary>
internal sealed class WhatsAppTraceInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Record(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        Record(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Record(DbContext? context)
    {
        if (context is null || WhatsAppTelemetry.Current() is not { } trace)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added))
        {
            switch (entry.Entity)
            {
                case WhatsAppMessage message:
                    message.RecordTrace(trace);
                    break;
                case WhatsAppOutgoingMessage outgoing:
                    outgoing.RecordTrace(trace);
                    break;
            }
        }
    }
}
