using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms;

/// <summary>F18: a new action run or publication operation keeps the trace context where it is saved, which its worker continues.</summary>
internal sealed class FormsTraceInterceptor : SaveChangesInterceptor
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
        if (context is null || FormsTelemetry.Current() is not { } trace)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added))
        {
            switch (entry.Entity)
            {
                case BusinessFormActionRun run:
                    run.RecordTrace(trace);
                    break;
                case BusinessFormPublicationOperation operation:
                    operation.RecordTrace(trace);
                    break;
            }
        }
    }
}
