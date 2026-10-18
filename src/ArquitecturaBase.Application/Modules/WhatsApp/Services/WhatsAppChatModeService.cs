using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppChatModeService(IWhatsAppConversationRepository repository, IRequestValidator validator,
    ICurrentUser currentUser, IUnitOfWork unitOfWork, IPermissionService permissions, IUserReader users,
    IAssistantOperations assistant, ILogger<WhatsAppChatModeService> logger) : IWhatsAppChatModeService
{
    public Task<Result<WhatsAppModeChangedResponse>> ChangeAsync(ChangeWhatsAppModeRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppModeChangedResponse>>(logger, "ChangeWhatsAppMode", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
            if (currentUser.UserId is not { } actor) return WhatsAppChatErrors.SessionRequired;
            var canceled = new List<(AssistantChannelContext Context, Guid RequestId)>();
            var result = await unitOfWork.ExecuteInTransactionAsync<Result<WhatsAppModeChangedResponse>>(async ct =>
            {
                var permission = request.Mode == WhatsAppConversationMode.Manual ? WhatsAppPermissions.Send : WhatsAppPermissions.ConfigureAssistant;
                if (await users.FindByIdAsync(actor, ct) is not { IsActive: true } || !await permissions.HasPermissionAsync(actor, permission, ct))
                    return WhatsAppChatErrors.Forbidden;
                var contact = await repository.GetForUpdateAsync(request.ContactId, ct);
                if (contact is null) return WhatsAppChatErrors.NotFound;
                if (contact.ModeRevision != request.ExpectedRevision) return WhatsAppChatErrors.RevisionConflict;
                if (request.Mode == WhatsAppConversationMode.Assistant && (await assistant.GetAvailabilityAsync(ct)).State != "available")
                    return WhatsAppChatErrors.AssistantUnavailable;
                contact.ChangeMode(request.Mode);
                if (request.Mode == WhatsAppConversationMode.Manual)
                {
                    foreach (var pending in await repository.ListPendingAutomaticRequestsAsync(contact.Id, ct))
                    {
                        if (!pending.IsAlways) canceled.Add((WhatsAppAssistantSuggestionPolicy.Context(WhatsAppAssistantSuggestionPolicy.Reference(pending)), pending.RequestId));
                    }
                }
                var assistantSendInProgress = request.Mode == WhatsAppConversationMode.Manual &&
                    await repository.ExistsStartedAssistantMessageAsync(contact.Id, ct);
                return new WhatsAppModeChangedResponse(contact.Mode, contact.ModeRevision, assistantSendInProgress);
            }, CommitPolicy.OnSuccess, cancellationToken);
            if (result.IsSuccess)
                foreach (var pending in canceled) await assistant.CancelAsync(pending.Context, pending.RequestId, cancellationToken);
            return result;
        });
}
