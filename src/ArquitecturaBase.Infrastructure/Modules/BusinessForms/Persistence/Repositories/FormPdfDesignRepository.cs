using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;
internal sealed class FormPdfDesignRepository(ApplicationDbContext dbContext) : IFormPdfDesignRepository
{
    public Task<BusinessFormPdfDesign?> GetAsync(Guid id, CancellationToken cancellationToken) => dbContext.Set<BusinessFormPdfDesign>().SingleOrDefaultAsync(design => design.Id == id, cancellationToken);
    public void Add(BusinessFormPdfDesign design) => dbContext.Set<BusinessFormPdfDesign>().Add(design);
}
