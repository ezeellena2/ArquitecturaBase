using ArquitecturaBase.Application.Models.StructuredData;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

public interface IWhatsAppFormActionService
{
    Task<StructuredRecordActionResponse> EnqueueAsync(StructuredRecordActionMessage request, CancellationToken cancellationToken);
    Task<StructuredRecordActionResponse> GetAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken);
    Task<StructuredRecordActionResponse> ReviewAsync(Guid runId, Guid recipientUserId, StructuredRecordActionReview review, CancellationToken cancellationToken);
    Task<StructuredRecordActionResponse> RetryAsync(Guid runId, Guid recipientUserId, long controlRevision, CancellationToken cancellationToken);
}
