using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class AssistantCapabilityExecutorProbe
{
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; init; } = [Definition(1), Definition(2)];
    public List<Guid> CreatedScopes { get; } = [];
    public List<Guid> DisposedScopes { get; } = [];
    public List<AssistantCapabilityRequest> Requests { get; } = [];
    public bool ThrowOnExecute { get; init; }
    public bool FailOnExecute { get; init; }

    public static AssistantCapabilityDefinition Definition(int version) => new("fictional.read", version,
        AssistantCapabilityKind.Read, "Fictional local read", "{}", "{}", new([], []), new(100, 100, 1, 1));
}

internal sealed class AssistantCapabilityExecutorSource : IAssistantCapabilitySource, IAsyncDisposable
{
    private readonly AssistantCapabilityExecutorProbe probe;
    private readonly Guid scopeId = Guid.NewGuid();

    public AssistantCapabilityExecutorSource(AssistantCapabilityExecutorProbe probe)
    {
        this.probe = probe;
        probe.CreatedScopes.Add(scopeId);
    }

    public IReadOnlyList<AssistantCapabilityDefinition> Definitions => probe.Definitions;

    public Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
    {
        probe.Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (probe.ThrowOnExecute) throw new InvalidOperationException("Synthetic capability failure.");
        return Task.FromResult(probe.FailOnExecute
            ? Result.Failure<AssistantCapabilityResponse>(SystemQueryErrors.Forbidden)
            : Result.Success(new AssistantCapabilityResponse("complete", new AssistantSystemQueryPayload("complete", null), [], 0)));
    }

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
        SystemQueryErrors.InvalidArguments;

    public ValueTask DisposeAsync()
    {
        probe.DisposedScopes.Add(scopeId);
        return ValueTask.CompletedTask;
    }
}
