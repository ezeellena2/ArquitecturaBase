namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>Whether a response has a live issuance: issued, being issued or in review, not failed nor not applicable. Only reads.</summary>
public interface IFormRecordIssuanceReader
{
    Task<bool> ExistsLiveAsync(Guid recordId, CancellationToken cancellationToken);
}
