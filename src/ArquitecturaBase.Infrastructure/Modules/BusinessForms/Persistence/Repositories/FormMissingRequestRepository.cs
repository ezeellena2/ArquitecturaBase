using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;

internal sealed class FormMissingRequestRepository(ApplicationDbContext dbContext) : IFormMissingRequestRepository
{
    public Task LockAsync(CancellationToken ct) => dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.MissingRequests],ct);
    public Task<BusinessFormMissingRequest?> GetRequestAsync(Guid sourceReceiptId,CancellationToken ct) =>
        dbContext.Set<BusinessFormMissingRequest>().SingleOrDefaultAsync(request => request.SourceReceiptId == sourceReceiptId,ct);
    public Task<BusinessFormMissingTopic?> GetAsync(Guid topicId,CancellationToken ct) =>
        dbContext.Set<BusinessFormMissingTopic>().SingleOrDefaultAsync(topic => topic.Id == topicId,ct);
    public Task<BusinessFormMissingTopic?> GetByNameAsync(string normalizedName,CancellationToken ct) =>
        dbContext.Set<BusinessFormMissingTopic>().SingleOrDefaultAsync(topic => topic.NormalizedName == normalizedName,ct);
    public void Add(BusinessFormMissingTopic topic) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormMissingTopic>().Add(topic); }
    public void Add(BusinessFormMissingRequest request) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormMissingRequest>().Add(request); }
}
