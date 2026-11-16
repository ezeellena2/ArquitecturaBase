namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>F2: tells whoever loaded a response, once, that an action stopped its rule; the worker calls it when it meets a run that cannot go on.</summary>
public interface IFormActionStallService
{
    Task NotifyAsync(Guid runId, CancellationToken cancellationToken);
}
