using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

/// <summary>Configuración › Formularios: each number with its current value, and the revision a save brings back.</summary>
public sealed record FormSettingsResponse(int MaxFileSizeMiB, int DailyRereadsPerAsker, int SimilarDocumentsToSuggest, int ProposedExpiryDays,
    int DiscardedRetentionDays, long Revision)
{
    public static FormSettingsResponse From(BusinessFormsSettings settings) => new(settings.MaxFileSizeMiB, settings.DailyRereadsPerAsker,
        settings.SimilarDocumentsToSuggest, settings.ProposedExpiryDays, settings.DiscardedRetentionDays, settings.Revision);
}

public sealed record UpdateFormSettingsRequest(long? ExpectedRevision, int? MaxFileSizeMiB, int? DailyRereadsPerAsker, int? SimilarDocumentsToSuggest,
    int? ProposedExpiryDays, int? DiscardedRetentionDays);
