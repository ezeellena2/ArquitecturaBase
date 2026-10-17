using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Channels;

internal sealed class WhatsAppStructuredRecordActionChannel(IWhatsAppFormActionService actions) : IStructuredRecordActionChannel
{
    public string ChannelId => "phone";
    public Task<StructuredRecordActionResponse> EnqueueAsync(StructuredRecordActionMessage request, CancellationToken cancellationToken) => actions.EnqueueAsync(request, cancellationToken);
    public Task<StructuredRecordActionResponse> GetAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken) => actions.GetAsync(runId, recipientUserId, cancellationToken);
    public Task<StructuredRecordActionResponse> ReviewAsync(Guid runId, Guid recipientUserId, StructuredRecordActionReview review, CancellationToken cancellationToken) => actions.ReviewAsync(runId, recipientUserId, review, cancellationToken);
    public Task<StructuredRecordActionResponse> RetryAsync(Guid runId, Guid recipientUserId, long controlRevision, CancellationToken cancellationToken) => actions.RetryAsync(runId, recipientUserId, controlRevision, cancellationToken);
}
