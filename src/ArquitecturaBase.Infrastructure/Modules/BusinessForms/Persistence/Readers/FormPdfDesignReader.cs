using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed class FormPdfDesignReader(ApplicationDbContext dbContext, IFormRuleReader definitions) : IFormPdfDesignReader
{
    public Task<FormRuleDefinitionRow?> FindDefinitionAsync(Guid definitionId, CancellationToken cancellationToken) => definitions.FindDefinitionAsync(definitionId, cancellationToken);
    public Task<FormPdfDesignStoredRow?> FindCurrentAsync(Guid definitionId, CancellationToken cancellationToken) => Project(
        from definition in dbContext.Set<BusinessFormDefinition>().AsNoTracking()
        join design in dbContext.Set<BusinessFormPdfDesign>().AsNoTracking() on definition.ActivePdfDesignId equals design.Id
        where definition.Id == definitionId select design).SingleOrDefaultAsync(cancellationToken);
    public Task<FormPdfDesignStoredRow?> FindRevisionAsync(Guid definitionId, long revision, CancellationToken cancellationToken) => Project(dbContext.Set<BusinessFormPdfDesign>().AsNoTracking()
        .Where(design => design.DefinitionId == definitionId && design.Revision == revision)).SingleOrDefaultAsync(cancellationToken);
    public Task<FormPdfDocumentRow?> FindDocumentAsync(Guid recordId, Guid documentId, CancellationToken cancellationToken) => (
        from document in dbContext.Set<BusinessFormDocument>().AsNoTracking()
        join file in dbContext.Set<StoredFile>().AsNoTracking() on document.FileId equals file.Id
        where document.RecordId == recordId && document.Id == documentId
        select new FormPdfDocumentRow(document.Id, document.RecordId, document.Culture, file.Name, file.StorageKey)).SingleOrDefaultAsync(cancellationToken);
    private IQueryable<FormPdfDesignStoredRow> Project(IQueryable<BusinessFormPdfDesign> designs) => designs.Select(design => new FormPdfDesignStoredRow(
        design.Id, design.DefinitionId, design.Revision, design.OriginalCulture, design.DesignJson, design.CreatedAtUtc,
        design.CreatedBy == null ? null : new FormAuthorResponse(design.CreatedBy.Value, dbContext.Users.Where(user => user.Id == design.CreatedBy).Select(user => user.DisplayName).FirstOrDefault())));
}
