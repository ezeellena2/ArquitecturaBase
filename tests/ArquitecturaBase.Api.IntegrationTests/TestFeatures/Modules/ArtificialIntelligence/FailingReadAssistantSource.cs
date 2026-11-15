using System.Data.Common;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

/// <summary>I5: a read whose database fails every time, as a source sees an outage of PostgreSQL. It never answers.</summary>
internal sealed class FailingReadAssistantSource : IAssistantCapabilitySource
{
    private const string Schema = """{"type":"object","properties":{},"required":[],"additionalProperties":false}""";
    public static readonly AssistantCapabilityReference Reference = new("fixture.read", 1);
    public int Executions { get; private set; }
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions => [new(Reference.Id, Reference.Version, AssistantCapabilityKind.Read,
        "Fictional read whose database fails", Schema, Schema, new([], []), new(4096, 16384, 5, 5))];

    public Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
    {
        Executions++;
        throw new FictionalDbException();
    }

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) => AiAssistantErrors.InvalidOutput;
}

/// <summary>
/// A database failure as Npgsql raises it (a <see cref="DbException"/>, transient or not, with its SQLSTATE), with a private detail that
/// never reaches a log or a turn.
/// </summary>
internal sealed class FictionalDbException(bool transient = false, string? sqlState = null) : DbException("private-database-detail of a fictional outage")
{
    public override bool IsTransient => transient;
    public override string? SqlState => sqlState;
}
