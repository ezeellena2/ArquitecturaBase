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
                    if(result.IsFailure||result.Value is null)continue;work=result.Value;
                }
                FormActionOutcomeResponse outcome;
                await using(var effect=scopes.CreateAsyncScope())outcome=await effect.ServiceProvider.GetRequiredService<IFormActionEffectService>().ExecuteAsync(work,cancellationToken);
                await using var completion=scopes.CreateAsyncScope();
                await completion.ServiceProvider.GetRequiredService<IFormActionDispatchService>().CompleteAsync(work,outcome,cancellationToken);
            }
            catch(Exception exception)when(!cancellationToken.IsCancellationRequested){LogUnknown(logger,exception.GetType().Name);}
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!options.Value.ProcessInBackground)return;
        while(!stoppingToken.IsCancellationRequested)
        {
            try{await ProcessNextAsync(null,stoppingToken);}
            catch(Exception exception)when(!stoppingToken.IsCancellationRequested){LogUnknown(logger,exception.GetType().Name);}
            await Task.Delay(TimeSpan.FromSeconds(2),timeProvider,stoppingToken);
        }
    }
    [LoggerMessage(Level=LogLevel.Warning,Message="The form action outcome could not be confirmed after {ExceptionType}; durable recovery will inspect it.")]
    private static partial void LogUnknown(ILogger logger,string exceptionType);
}
