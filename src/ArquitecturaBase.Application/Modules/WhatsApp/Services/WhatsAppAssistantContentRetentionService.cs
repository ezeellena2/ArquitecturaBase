using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// W3: in the same batch, what a send took of what the person shared goes with the retention of the messages; H13: and the point and
// the body a send kept, once it was captured or its time ran out without a reply on its way.
internal sealed class WhatsAppAssistantContentRetentionService(IWhatsAppMessageRetentionRepository repository,IUnitOfWork unitOfWork,
    TimeProvider clock,IWhatsAppSettingsReader settings,ILogger<WhatsAppAssistantContentRetentionService> logger) : IWhatsAppAssistantContentRetentionService
{
    public Task<Result<int>> ClearAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<int>>(logger,"ClearExpiredWhatsAppAssistantContent",()=>
            unitOfWork.ExecuteInTransactionAsync(async ct=>
            {
                var nowUtc=clock.GetUtcNow().UtcDateTime;
                var assistant=await repository.ClearExpiredAssistantContentAsync(nowUtc,500,ct);
                var days=(await settings.FindAsync(ct)).MessageRetentionDays;
                var shared=await repository.ClearExpiredSharedValuesAsync(nowUtc.AddDays(-days),500,ct);
                return Result.Success(assistant+shared+await repository.ClearExpiredSendContentAsync(nowUtc,500,ct));
            },CommitPolicy.OnSuccess,cancellationToken));
}
