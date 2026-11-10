using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;

/// <summary>
/// Corre el robot entre los pasos guardados del almacén, como el procesador de la emisión: cada paso en un scope propio, con
/// su DbContext y su transacción, y el navegador entre uno y otro, sin transacción (ADR 0026). Un error que no es un motivo
/// del robot es un bug: se registra el tipo y el lado queda frenado por pantalla cambiada, que avisa al dueño.
/// </summary>
internal sealed partial class ArcaRobotProcessor(IServiceScopeFactory scopes, IArcaDelegationRobot robot, TimeProvider timeProvider,
    ILogger<ArcaRobotProcessor> logger)
{
    /// <summary>El lado del cliente, con la clave que llegó en memoria; al volver, nadie más la tiene.</summary>
    public async Task RunCompanyAsync(ArcaCompanyRobotWorkResponse job, ArcaFiscalCredentials credentials, CancellationToken cancellationToken)
    {
        var progress = new Progress(this, job.RunId, job.Owner);
        ArcaRobotOutcome outcome;
        try
        {
            outcome = await robot.RunCompanyAsync(job, credentials, progress, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(exception.GetType().Name, ArcaRobotSide.Company);
            outcome = ArcaRobotOutcome.Stopped(ArcaRobotFailure.ScreenChanged, progress.Current ?? ArcaRobotSteps.CompanyLogin);
        }

        await StepAsync(store => store.FinishCompanyAsync(job.RunId, job.Owner, outcome, cancellationToken));
    }

    /// <summary>Los lados de la plataforma vencidos, de a uno; una falla de uno no frena a los demás.</summary>
    public async Task ProcessDuePlatformAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<IArcaRobotRunReader>()
                .ListDuePlatformRunsAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        }

        foreach (var runId in due)
        {
            try
            {
                await ProcessPlatformAsync(runId, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogFailed(exception.GetType().Name, ArcaRobotSide.Platform);
            }
        }
    }

    public async Task ProcessPlatformAsync(Guid runId, CancellationToken cancellationToken)
    {
        var owner = Guid.NewGuid();
        var job = await StepAsync(store => store.ReservePlatformAsync(runId, owner, cancellationToken));
        if (job is null) return;

        var progress = new Progress(this, runId, owner);
        ArcaRobotOutcome outcome;
        try
        {
            outcome = await robot.RunPlatformAsync(job, progress, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailed(exception.GetType().Name, ArcaRobotSide.Platform);
            outcome = ArcaRobotOutcome.Stopped(ArcaRobotFailure.ScreenChanged, progress.Current ?? ArcaRobotSteps.PlatformLogin);
        }

        // Con el cliente listo, la detección de la etapa 5 confirma: la prueba de la conexión, fuera de toda transacción.
        Result<ArcaTestResponse>? verification = null;
        if (outcome is ArcaRobotOutcome.Succeeded && job.CompanyDone)
        {
            await using var scope = scopes.CreateAsyncScope();
            verification = await scope.ServiceProvider.GetRequiredService<IArcaConnectionTestService>().TestAsync(cancellationToken);
        }

        await StepAsync(store => store.FinishPlatformAsync(runId, owner, outcome, verification, cancellationToken));
    }

    private async Task<T> StepAsync<T>(Func<IArcaRobotRunStore, Task<T>> step)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await step(scope.ServiceProvider.GetRequiredService<IArcaRobotRunStore>());
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The ARCA robot failed with {ExceptionType} on the {Side} side.")]
    private partial void LogFailed(string exceptionType, ArcaRobotSide side);

    /// <summary>Lo que avisa el robot, cada aviso en su scope y su transacción.</summary>
    private sealed class Progress(ArcaRobotProcessor processor, Guid runId, Guid owner) : IArcaRobotProgress
    {
        public string? Current { get; private set; }

        public Task<bool> BeginAsync(string step, CancellationToken cancellationToken)
        {
            Current = step;
            return processor.StepAsync(store => store.BeginStepAsync(runId, owner, step, cancellationToken));
        }

        public Task<bool> CompleteAsync(string step, string? output, CancellationToken cancellationToken) =>
            processor.StepAsync(store => store.CompleteStepAsync(runId, owner, step, output, cancellationToken));

        public Task<bool> InstallCertificateAsync(string certificatePem, CancellationToken cancellationToken) =>
            processor.StepAsync(store => store.InstallPlatformCertificateAsync(runId, owner, certificatePem, cancellationToken));
    }
}
