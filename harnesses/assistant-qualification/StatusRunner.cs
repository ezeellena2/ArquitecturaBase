using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

namespace AssistantQualification;

internal sealed class StatusRunner(QualificationCurrentUser currentUser, IAiAssistantService assistant, TimeProvider clock)
{
    public async Task<AiAssistantTurnResponse?> WaitAsync(QualificationLedgerEntry entry, CancellationToken cancellationToken)
    {
        currentUser.Set(entry.ActorUserId);
        var deadline = clock.GetUtcNow().AddSeconds(240);
        while (clock.GetUtcNow() < deadline)
        {
            var result = await assistant.GetTurnAsync(entry.RequestId, cancellationToken);
            // A before-submit ledger can have a request never accepted. Do not resubmit, reset quota or invent success.
            if (result.IsFailure) return null;
            if (result.Value.State is not ("pending" or "running")) return result.Value;
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(result.Value.PollingAfterSeconds, 1, 30)), clock, cancellationToken);
        }
        return null;
    }
}
