using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>
/// The requests of «Armar formulario con la IA» (DOC6) to change them: tracked, never saved here. <see cref="LockRequestAsync"/> serializes
/// asking for one request of one person; <see cref="LockAskerAsync"/>, the reservations of a person's re-reads (their daily share), before
/// <see cref="LockAsync"/>, every change to a request, itself before the lock of any of its documents.
/// </summary>
public interface IFormDocumentProposalRepository
{
    Task LockRequestAsync(Guid actorUserId, Guid requestId, CancellationToken cancellationToken);
    Task LockAsync(Guid proposalId, CancellationToken cancellationToken);
    Task LockAskerAsync(Guid actorUserId, CancellationToken cancellationToken);
    /// <summary>How many re-reads the engine took for the requests of that person on that UTC day.</summary>
    Task<int> CountRereadsOnDayAsync(Guid actorUserId, DateOnly day, CancellationToken cancellationToken);
    /// <summary>Whether another request of that person has documents pending held at <paramref name="nowUtc"/>: by its round, or waiting for the engine.</summary>
    Task<bool> ExistsRereadHeldAsync(Guid actorUserId, Guid exceptProposalId, DateTime nowUtc, CancellationToken cancellationToken);
    Task<FormDocumentProposal?> GetAsync(Guid proposalId, CancellationToken cancellationToken);
    Task<FormDocumentProposal?> GetByRequestAsync(Guid actorUserId, Guid requestId, CancellationToken cancellationToken);
    void Add(FormDocumentProposal proposal);
}
