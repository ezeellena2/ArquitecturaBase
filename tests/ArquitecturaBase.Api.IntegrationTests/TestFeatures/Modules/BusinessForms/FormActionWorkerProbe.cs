using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
internal sealed class FormActionWorkerProbe(bool throwOnStalled)
{
    public Guid[] RunIds { get; } = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
    public List<Guid> Reserved { get; } = [];
    public List<Guid> Completed { get; } = [];
    public TaskCompletionSource BatchCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Result<FormActionWorkResponse?> Reserve(Guid id)
    {
        Reserved.Add(id);
        if (id == RunIds[3]) { Complete(id); return Result.Success<FormActionWorkResponse?>(null); }
        return Result.Success<FormActionWorkResponse?>(new(id, id, 1, false, false, null!, []));
    }
    public FormActionOutcomeResponse Execute(Guid id)
    {
        if (Array.IndexOf(RunIds, id) < 3)
        {
            if (throwOnStalled) throw new TimeoutException("private-effect-data");
            return new(FormActionRunState.Uncertain);
        }
        return new(FormActionRunState.Completed);
    }
    public void Complete(Guid id)
    {
        Completed.Add(id);
        if (RunIds.Skip(3).All(Completed.Contains)) BatchCompleted.TrySetResult();
    }
}
internal sealed class FormActionWorkerDispatch(FormActionWorkerProbe probe) : IFormActionDispatchService
{
    public Task<IReadOnlyList<Guid>> ListDueIdsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>(probe.RunIds);
    public Task<Result<FormActionWorkResponse?>> ReserveAsync(Guid runId, CancellationToken cancellationToken) => Task.FromResult(probe.Reserve(runId));
    public Task<Result> CompleteAsync(FormActionWorkResponse work, FormActionOutcomeResponse outcome, CancellationToken cancellationToken)
    { probe.Complete(work.RunId); return Task.FromResult(Result.Success()); }
}
internal sealed class FormActionWorkerEffect(FormActionWorkerProbe probe) : IFormActionEffectService
{
    public Task<FormActionOutcomeResponse> ExecuteAsync(FormActionWorkResponse work, CancellationToken cancellationToken) => Task.FromResult(probe.Execute(work.RunId));
}
