using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
internal sealed partial class FormActionBackgroundService(IServiceScopeFactory scopes,TimeProvider timeProvider,
    IOptions<FormActionWorkerOptions> options,ILogger<FormActionBackgroundService> logger):BackgroundService
{
    public async Task ProcessNextAsync(Guid? runId,CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids;
        if(runId is {} id)ids=[id];
        else{await using var scope=scopes.CreateAsyncScope();ids=await scope.ServiceProvider.GetRequiredService<IFormActionDispatchService>().ListDueIdsAsync(cancellationToken);}
        foreach(var candidate in ids)
        {
            try
            {
                FormActionWorkResponse? work;
                await using(var reservation=scopes.CreateAsyncScope())
                {
                    var result=await reservation.ServiceProvider.GetRequiredService<IFormActionDispatchService>().ReserveAsync(candidate,cancellationToken);
                    // F2: a run that cannot go on (failed, or waiting for a person) is told once to whoever loaded the response.
                    if(result.IsSuccess&&result.Value is null&&reservation.ServiceProvider.GetService<IFormActionStallService>() is {} stalls)await stalls.NotifyAsync(candidate,cancellationToken);
                    if(result.IsFailure||result.Value is null)continue;work=result.Value;
                }
                // F18: the run's effect and its result happen inside the trace where the run was created.
                using var activity=FormsTelemetry.StartAction(work.RunId,work.Kind,work.TraceParent);
                FormActionOutcomeResponse outcome;
                await using(var effect=scopes.CreateAsyncScope())outcome=await effect.ServiceProvider.GetRequiredService<IFormActionEffectService>().ExecuteAsync(work,cancellationToken);
                await using var completion=scopes.CreateAsyncScope();
                await completion.ServiceProvider.GetRequiredService<IFormActionDispatchService>().CompleteAsync(work,outcome,cancellationToken);
            }
            catch(Exception exception)when(!cancellationToken.IsCancellationRequested){LogUnknown(logger,candidate,exception.GetType().Name);}
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!options.Value.ProcessInBackground)return;
        while(!stoppingToken.IsCancellationRequested)
        {
            try{await ProcessNextAsync(null,stoppingToken);}
            catch(Exception exception)when(!stoppingToken.IsCancellationRequested){LogCycleFailed(logger,exception.GetType().Name);}
            await Task.Delay(TimeSpan.FromSeconds(2),timeProvider,stoppingToken);
        }
    }
    [LoggerMessage(Level=LogLevel.Warning,Message="The outcome of form action run {RunId} could not be confirmed after {ExceptionType}; durable recovery will inspect it.")]
    private static partial void LogUnknown(ILogger logger,Guid runId,string exceptionType);
    [LoggerMessage(Level=LogLevel.Warning,Message="A round of form actions failed after {ExceptionType}; the next one tries again.")]
    private static partial void LogCycleFailed(ILogger logger,string exceptionType);
}
