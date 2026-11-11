namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>Whether a response has a live issuance (<see cref="Domain.Modules.BusinessForms.BusinessFormActionRun.LiveIssuance"/>, the one definition). Only reads.</summary>
public interface IFormRecordIssuanceReader
{
    Task<bool> ExistsLiveAsync(Guid recordId, CancellationToken cancellationToken);
}
