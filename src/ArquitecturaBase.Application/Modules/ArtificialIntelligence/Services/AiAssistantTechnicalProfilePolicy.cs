using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

// The technical purposes (NormalizeAudio and ReadDocument) take their model from a profile of their own, sealed at acceptance by its
// exact Id and version without changing AiConnection.Model, and read a private file. The access guard delegates them here.
internal sealed class AiAssistantTechnicalProfilePolicy(IReadOnlyCollection<AiAssistantAudioModelProfile> audioProfiles,
    IReadOnlyCollection<AiAssistantDocumentModelProfile> documentProfiles, IPrivateFileStore files)
{
    // IA4: the browser records WebM or MP4; Home offers the microphone only when this connection hears a whole recording and
    // there is private storage to keep it.
    public bool HearsHomeAudio(AiConnectionRow connection) => files.IsAvailable && connection.Enabled && connection.ProtectedApiKey is not null
        && audioProfiles.Any(p => p.Provider == connection.Provider && p.MaximumAudioBytes >= SubmitAiAssistantAudioRequest.MaximumBytes
            && p.ContentTypes.Any(type => type is "audio/webm" or "audio/mp4"));

    public Result<AiAssistantExecutionProfileSnapshot> CaptureAudio(AiConnectionRow connection, AssistantChannelContext channel,
        AssistantAudioInput audio, DateTime occurredAtUtc)
    {
        if (!connection.Enabled || connection.ProtectedApiKey is null || connection.Provider != AiProvider.OpenAI) return AiAssistantErrors.Unavailable;
        var profile = audioProfiles.Where(p => p.Provider == connection.Provider && p.MaximumAudioBytes >= audio.Size &&
            p.ContentTypes.Contains(AiAssistantAudioPolicy.MediaType(audio), StringComparer.Ordinal)).OrderByDescending(p => p.Version)
            .ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
        return profile is null ? AiAssistantErrors.Unavailable : new AiAssistantExecutionProfileSnapshot(profile.Id, profile.Version,
            "audio-none-v1", 1, channel) { Purpose = AiAssistantPurpose.NormalizeAudio, OccurredAtUtc = occurredAtUtc };
    }

    public AiAssistantAudioModelProfile? Audio(AiAssistantExecutionProfileSnapshot profile, AiConnectionRow connection) =>
        audioProfiles.SingleOrDefault(p => p.Id == profile.ModelProfileId && p.Version == profile.ModelProfileVersion && p.Provider == connection.Provider);

    // The installation reads documents with its saved connection enabled and with a key, a profile that covers a PDF, a JPEG and a PNG at
    // the attachment's limits, and private storage for the copies.
    public bool ReadsDocuments(AiConnectionRow connection) => files.IsAvailable && connection.Enabled && connection.ProtectedApiKey is not null
        && documentProfiles.Any(p => p.Provider == connection.Provider && p.MaximumDocumentBytes >= StructuredAttachmentLimits.MaximumBytes
            && p.MaximumPages >= StructuredAttachmentLimits.MaximumPages
            && AiAssistantDocumentPolicy.MediaTypes.All(type => p.ContentTypes.Contains(type, StringComparer.Ordinal)));

    public Result<AiAssistantExecutionProfileSnapshot> CaptureDocument(AiConnectionRow connection, AssistantChannelContext channel,
        AssistantDocumentInput document, DateTime occurredAtUtc)
    {
        if (!connection.Enabled || connection.ProtectedApiKey is null || connection.Provider != AiProvider.OpenAI) return AiAssistantErrors.Unavailable;
        var profile = documentProfiles.Where(p => p.Provider == connection.Provider && AiAssistantDocumentPolicy.Fits(document, p))
            .OrderByDescending(p => p.Version).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
        return profile is null ? AiAssistantErrors.Unavailable : new AiAssistantExecutionProfileSnapshot(profile.Id, profile.Version,
            AiAssistantDocumentPolicy.InstructionsId, AiAssistantDocumentPolicy.InstructionsVersion, channel)
            { Purpose = AiAssistantPurpose.ReadDocument, OccurredAtUtc = occurredAtUtc };
    }

    public AiAssistantDocumentModelProfile? Document(AiAssistantExecutionProfileSnapshot profile, AiConnectionRow connection) =>
        documentProfiles.SingleOrDefault(p => p.Id == profile.ModelProfileId && p.Version == profile.ModelProfileVersion && p.Provider == connection.Provider);
}
