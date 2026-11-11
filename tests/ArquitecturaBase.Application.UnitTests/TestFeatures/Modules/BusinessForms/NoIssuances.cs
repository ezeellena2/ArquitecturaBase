using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

/// <summary>Responses without issuances: the corrections these tests make never meet an issued document.</summary>
internal sealed class NoIssuances : IFormRecordIssuanceReader
{
    public Task<bool> ExistsLiveAsync(Guid recordId, CancellationToken cancellationToken) => Task.FromResult(false);
}
