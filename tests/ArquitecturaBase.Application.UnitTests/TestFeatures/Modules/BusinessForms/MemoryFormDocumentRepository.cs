using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
internal sealed class MemoryFormDocumentRepository : IFormDocumentRepository
{
    public List<BusinessFormDocument> Documents { get; } = [];
    public void Add(BusinessFormDocument document) => Documents.Add(document);
    public Task<IReadOnlyList<BusinessFormDocument>> ListForRunAsync(Guid runId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BusinessFormDocument>>(Documents.Where(document => document.RunId == runId).ToArray());
}
