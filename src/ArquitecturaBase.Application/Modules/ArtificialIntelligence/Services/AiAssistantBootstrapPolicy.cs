using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

// What Home offers this person: the availability, the core suggestions, the public limit and what the composer takes besides text (a
// recording, IA4; a spreadsheet, Stage 2; a Word, and a photo or a PDF where the installation reads them, Stage 3). Who may attach is the
// owner's rule through the same policy as a turn, so the front never repeats the permissions.
internal sealed class AiAssistantBootstrapPolicy(IAiAssistantReader reader, IAiConnectionReader connectionReader, IPermissionService permissions,
    AiAssistantAccessGuard guard, AiAssistantResponsePolicy responsePolicy, AiAssistantCapabilityCatalogPolicy catalog, AiAssistantLimits limits,
    AiAssistantHomeAttachmentPolicy attachments)
{
    public async Task<Result<AiAssistantBootstrapResponse>> BootstrapAsync(CancellationToken cancellationToken)
    {
        if (guard.CurrentUserId is not { } userId) return AiAssistantErrors.SessionRequired;
        var actor = await guard.AuthorizeAsync(userId, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var connection = await connectionReader.FindAsync(cancellationToken);
        var current = await permissions.GetPermissionsAsync(userId, cancellationToken);
        var context = guard.HomeContext(userId, "es", actor.Value.TimeZoneId);
        var offered = await catalog.BuildAsync(context, cancellationToken);
        if (offered.IsFailure) return offered.Error;
        // Only the core queries carry a curated suggestion; what an owner offers in Home brings its own entry points.
        var suggestions = offered.Value.Capabilities.Where(d => AiAssistantChannelPolicy.IsHomeQuery(d.Id))
            .Select(d => new AiAssistantSuggestion(d.Id, AssistantTexts.Get("Suggestion." + d.Id))).ToArray();
        var availability = guard.Availability(connection);
        if (availability == "available")
        {
            var workload = await reader.FindWorkloadAsync(responsePolicy.ServerNowUtc, limits.LeaseGraceSeconds, cancellationToken);
            if (workload.Pending >= limits.PendingCapacity) availability = "capacityLimited";
        }
        var attach = await attachments.CanAttachAsync(userId, cancellationToken);
        return new AiAssistantBootstrapResponse(availability, connection.Revision, suggestions,
            new AiAssistantPublicLimits(limits.QuestionCharacters), current.Contains(ArtificialIntelligencePermissions.Read) ? "aiConfiguration" : null)
            { Audio = guard.HearsHomeAudio(connection), Attachments = attach.Spreadsheets, Documents = attach.Documents,
                DocumentReading = attach.Documents && guard.ReadsDocuments(connection) };
    }
}
