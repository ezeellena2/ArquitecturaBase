using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>
/// «Cómo viene leyendo» (DOC1), computed in SQL from the responses of a form whose capture origin carries the document they came
/// from: how many were confirmed since a moment, per question how many data the reading proposed and how many were confirmed without
/// changes, and the latest corrections, the value read and the one confirmed, with the schemas of their versions.
/// </summary>
public interface IFormDocumentReadingReader
{
    /// <summary>The form, not deleted, or null.</summary>
    Task<FormDocumentReadingDefinitionRow?> FindDefinitionAsync(Guid definitionId, CancellationToken cancellationToken);
    Task<FormDocumentReadingStatsRow> FindStatsAsync(Guid definitionId, DateTime sinceUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormDocumentReadingCorrectionRow>> ListCorrectionsAsync(Guid definitionId, DateTime sinceUtc, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, string>> ListVersionSchemasAsync(IReadOnlyCollection<Guid> versionIds, CancellationToken cancellationToken);
}
