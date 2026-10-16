using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormPdfDesignRepository
{
    Task<BusinessFormPdfDesign?> GetAsync(Guid id, CancellationToken cancellationToken);
    void Add(BusinessFormPdfDesign design);
}
