using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormDocumentRepository
{
    Task<IReadOnlyList<BusinessFormDocument>> ListForRunAsync(Guid runId, CancellationToken cancellationToken);
    void Add(BusinessFormDocument document);
}
