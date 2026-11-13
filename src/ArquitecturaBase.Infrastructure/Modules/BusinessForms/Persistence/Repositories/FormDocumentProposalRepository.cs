using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;

internal sealed class FormDocumentProposalRepository(ApplicationDbContext dbContext) : IFormDocumentProposalRepository
{
    public Task LockRequestAsync(Guid actorUserId, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.DocumentProposalRequest(actorUserId, requestId)], cancellationToken);

    public Task LockAsync(Guid proposalId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.DocumentProposal(proposalId)], cancellationToken);

    public Task LockAskerAsync(Guid actorUserId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.DocumentRereadAsker(actorUserId)], cancellationToken);

    public Task<int> CountRereadsOnDayAsync(Guid actorUserId, DateOnly day, CancellationToken cancellationToken) =>
        dbContext.Set<FormDocumentProposal>().Where(proposal => proposal.ActorUserId == actorUserId && proposal.RereadDay == day)
            .SumAsync(proposal => proposal.RereadsOnDay, cancellationToken);

    public Task<bool> ExistsRereadHeldAsync(Guid actorUserId, Guid exceptProposalId, DateTime nowUtc, CancellationToken cancellationToken) =>
        dbContext.Set<FormDocumentProposal>().AnyAsync(proposal => proposal.ActorUserId == actorUserId && proposal.Id != exceptProposalId
            && proposal.PendingRereadIds.Length > 0 && proposal.RereadAfterUtc > nowUtc, cancellationToken);

    public Task<FormDocumentProposal?> GetAsync(Guid proposalId, CancellationToken cancellationToken) =>
        dbContext.Set<FormDocumentProposal>().SingleOrDefaultAsync(proposal => proposal.Id == proposalId, cancellationToken);

    public Task<FormDocumentProposal?> GetByRequestAsync(Guid actorUserId, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.Set<FormDocumentProposal>().SingleOrDefaultAsync(proposal => proposal.ActorUserId == actorUserId && proposal.RequestId == requestId,
            cancellationToken);

    public void Add(FormDocumentProposal proposal)
    {
        dbContext.RequireTransaction();
        dbContext.Set<FormDocumentProposal>().Add(proposal);
    }
}
