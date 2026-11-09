using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>The results external actions left in responses (RE1-A), read in one batch per page or detail, without tracking.</summary>
public interface IFormRecordResultReader
{
    /// <summary>The results of these responses, by response and then by the time each one was recorded.</summary>
    Task<IReadOnlyList<FormRecordResultRow>> ListAsync(IReadOnlyList<Guid> recordIds, CancellationToken cancellationToken);
    /// <summary>The latest result of each source among the form's responses: it names the columns of the sheet and the Excel.</summary>
    Task<IReadOnlyList<FormRecordResultRow>> ListLatestBySourceAsync(Guid definitionId, CancellationToken cancellationToken);
}
