using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

namespace AssistantQualification;

internal sealed class StatusRunner(QualificationCurrentUser currentUser, IAiAssistantService assistant, IAssistantOperations operations, TimeProvider clock,
    IAiAssistantAudioService audio)
{
    public async Task<AssistantOperationResponse?> WaitNeutralAsync(QualificationLedgerEntry entry,CancellationToken cancellationToken)
    {
        currentUser.Set(entry.ActorUserId);var context=QualificationChannelContexts.Resume(entry);
        var deadline=clock.GetUtcNow().AddSeconds(240);
        while(clock.GetUtcNow()<deadline)
        {
            var result=await operations.GetAsync(context,entry.RequestId,cancellationToken);
            if(result.IsFailure)return null;
            if(result.Value.State is not ("pending" or "running" or "queued"))return result.Value;
            await Task.Delay(TimeSpan.FromSeconds(2),clock,cancellationToken);
        }
        return null;
    }
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
    // IA4: Get through the Home audio service of the same actor; it never stores or submits the recording again.
    public async Task<AiAssistantAudioResponse?> WaitHomeAudioAsync(QualificationLedgerEntry entry, CancellationToken cancellationToken)
    {
        if (!QualificationChannelContexts.IsHomeAudio(entry)) throw new InvalidOperationException("Invalid Home audio ledger scope.");
        currentUser.Set(entry.ActorUserId);
        var deadline = clock.GetUtcNow().AddSeconds(240);
        while (clock.GetUtcNow() < deadline)
        {
            var result = await audio.GetAsync(entry.RequestId, entry.Culture!, cancellationToken);
            if (result.IsFailure) return null;
            if (result.Value.State is not ("pending" or "running" or "queued")) return result.Value;
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(result.Value.PollingAfterSeconds, 1, 30)), clock, cancellationToken);
        }
        return null;
    }
    // document-reading: Get with the context its ledger names (QualificationDocuments.Resume); it never stores or submits the copy again.
    public async Task<AssistantOperationResponse?> WaitDocumentAsync(QualificationLedgerEntry entry, CancellationToken cancellationToken)
    {
        var context = QualificationDocuments.Resume(entry);
        currentUser.Set(entry.ActorUserId);
        var deadline = clock.GetUtcNow().AddSeconds(240);
        while (clock.GetUtcNow() < deadline)
        {
            var result = await operations.GetAsync(context, entry.RequestId, cancellationToken);
            if (result.IsFailure) return null;
            if (result.Value.State is not ("pending" or "running" or "queued")) return result.Value;
            await Task.Delay(TimeSpan.FromSeconds(2), clock, cancellationToken);
        }
        return null;
    }
}
