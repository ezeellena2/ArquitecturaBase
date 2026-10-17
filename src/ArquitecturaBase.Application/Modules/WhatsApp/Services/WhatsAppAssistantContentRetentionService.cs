using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppAssistantContentRetentionService(IWhatsAppMessageRetentionRepository repository,IUnitOfWork unitOfWork,
    TimeProvider clock,ILogger<WhatsAppAssistantContentRetentionService> logger) : IWhatsAppAssistantContentRetentionService
{
    public Task<Result<int>> ClearAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<int>>(logger,"ClearExpiredWhatsAppAssistantContent",()=>
            unitOfWork.ExecuteInTransactionAsync(async ct=>
                Result.Success(await repository.ClearExpiredAssistantContentAsync(clock.GetUtcNow().UtcDateTime,500,ct)),
                CommitPolicy.OnSuccess,cancellationToken));
}
