using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class AiAssistantCoordinatorProbe
{
    private int calls;
    private int failFinalCommit = 1;
    public int Calls => Volatile.Read(ref calls);
    public TaskCompletionSource CommitFailed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool OutsideTransaction { get; private set; } = true;
    public bool DurablyReserved { get; private set; } = true;

    public void Register(IServiceCollection services)
    {
        services.RemoveAll<ILlmConversationClient>();
        services.AddTransient<ILlmConversationClient>(provider => new QueryClient(provider.GetRequiredService<ApplicationDbContext>(), this));
        services.RemoveAll<IUnitOfWork>();
        services.AddScoped<IUnitOfWork>(provider => new FailOnceUnitOfWork(ActivatorUtilities.CreateInstance<UnitOfWork>(provider),
            provider.GetRequiredService<ApplicationDbContext>(), this));
    }

    private sealed class QueryClient(ApplicationDbContext db, AiAssistantCoordinatorProbe probe) : ILlmConversationClient
    {
        public async Task<Result<LlmConversationResponse>> SendAsync(LlmConversationRequest request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref probe.calls);
            probe.OutsideTransaction &= db.Database.CurrentTransaction is null;
            probe.DurablyReserved &= await db.Set<AiAssistantTurn>().AnyAsync(turn => turn.State == AiAssistantTurnState.Running && turn.ProviderSteps > 0, cancellationToken);
            if (request.ToolResults.Count == 0)
                return new LlmConversationResponse("tools", [new("query-" + call, "users.summary", "{}")], "fictional-continuation", new(10, 5));
            using var result = JsonDocument.Parse(request.ToolResults[0].ResultJson);
            var evidenceId = result.RootElement.GetProperty("evidence").GetProperty("id").GetGuid();
            var arguments = JsonSerializer.Serialize(new { kind = "answer", text = "Usuarios cargados.", evidenceIds = new[] { evidenceId } });
            return new LlmConversationResponse("tools", [new("answer-" + call, "assistant_answer", arguments)], "fictional-continuation", new(10, 5));
        }
    }

    private sealed class FailOnceUnitOfWork(UnitOfWork inner, ApplicationDbContext db, AiAssistantCoordinatorProbe probe) : IUnitOfWork
    {
        public Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CommitPolicy policy, CancellationToken cancellationToken)
            where TResult : Result => inner.ExecuteInTransactionAsync(async ct =>
            {
                var result = await work(ct);
                if (policy.Commits(result) && db.ChangeTracker.Entries<AiAssistantTurn>().Any(entry => entry.State == EntityState.Modified
                    && entry.Entity.State == AiAssistantTurnState.Completed) && Interlocked.Exchange(ref probe.failFinalCommit, 0) == 1)
                {
                    await db.SaveChangesAsync(ct);
                    probe.CommitFailed.TrySetResult();
                    throw new IOException("Fictional assistant commit failure.");
                }
                return result;
            }, policy, cancellationToken);
    }
}
