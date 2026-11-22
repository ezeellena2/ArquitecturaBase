using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

/// <summary>Configuración › Formularios (RC4-A): every number, and the revision read.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateFormSettingsHttpRequest(long? ExpectedRevision, int? MaxFileSizeMiB, int? DailyRereadsPerAsker, int? SimilarDocumentsToSuggest,
    int? ProposedExpiryDays, int? DiscardedRetentionDays);
