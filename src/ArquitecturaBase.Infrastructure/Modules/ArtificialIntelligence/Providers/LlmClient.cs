using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers;
internal sealed class LlmClient(IEnumerable<ILlmProviderClient> clients, IOptions<AiTransportOptions> options) : ILlmClient, IDisposable
{
    private readonly Dictionary<AiProvider, ILlmProviderClient> _clients = clients.ToDictionary(x => x.Provider);
    private readonly SemaphoreSlim _slots = new(options.Value.MaxConcurrentTests);
    public async Task<Result<IReadOnlyList<AiModelResponse>>> ListModelsAsync(LlmModelsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await _slots.WaitAsync(0, cancellationToken)) return LlmErrors.Busy;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            return _clients.TryGetValue(request.Provider, out var client)
                ? await client.ListModelsAsync(request, timeout.Token) : AiConnectionErrors.Invalid;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return LlmErrors.Unavailable; }
        finally { _slots.Release(); }
    }
    public async Task<Result<LlmTestResponse>> TestAsync(LlmTestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!await _slots.WaitAsync(0, cancellationToken)) return LlmErrors.Busy;
        try
        {
            return _clients.TryGetValue(request.Provider, out var client)
                ? await client.TestAsync(request, cancellationToken) : AiConnectionErrors.Invalid;
        }
        finally { _slots.Release(); }
    }
    public void Dispose() => _slots.Dispose();
}
