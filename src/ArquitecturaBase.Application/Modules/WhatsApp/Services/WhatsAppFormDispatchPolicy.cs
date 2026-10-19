using System.Security.Cryptography;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFormDispatchPolicy(IWhatsAppFormRecipientReader recipients, IStructuredFormSource forms,
    IWhatsAppFlowEnvironment environment, IPermissionService permissions, IPhoneNumberParser phoneNumbers,
    IWhatsAppFlowProtector projections, IUserReader users, TimeProvider clock)
{
    public async Task<Result<WhatsAppFlowMessageResponse>> PrepareAsync(WhatsAppFormDispatchRow row,
        WhatsAppFlowSessionPayload payload, WhatsAppFlowAssistantContent? content, CancellationToken ct)
    {
        if (row.ExpiresAtUtc <= clock.GetUtcNow().UtcDateTime) return FlowErrors.StateConflict;
        var actor = row.OperatorUserId ?? row.UserId;
        if (row.OperatorUserId is {} sender && (await users.FindByIdAsync(sender,ct) is not { IsActive:true }
            || !await permissions.HasPermissionAsync(sender,WhatsAppPermissions.Read,ct)
            || !await permissions.HasPermissionAsync(sender,WhatsAppPermissions.Send,ct))) return WhatsAppChatErrors.SendPending;
        if (row.OperatorUserId is null && row.Origin != WhatsAppMessageOrigin.Assistant) return FlowErrors.StateConflict;
        var recipient = await recipients.FindAsync(row.ContactId,ct);
        if (recipient?.UserId != row.UserId || !recipient.IsActive
            || WhatsAppFormRecipientRevision.Create(actor,recipient) != payload.RecipientRevision) return FlowErrors.RecipientContextChanged;
        if (await forms.FindAcceptedSessionAsync(row.UserId,row.Culture,row.DefinitionId,row.VersionId,ct) is null) return FlowErrors.FormUnavailable;
        var initial = InitialValues(row,payload,content);
        if (initial.IsFailure) return initial.Error;
        var account = await environment.VerifyAccountAsync(ct);
        if (account.IsFailure) return account.Error;
        if (!account.Value.HasSameIdentity(payload.Account)) return FlowErrors.AccountMismatch;
        var phone = WhatsAppIds.Parse(phoneNumbers,row.WaId);
        return phone.IsFailure ? WhatsAppChatErrors.DestinationMissing :
            new WhatsAppFlowMessageResponse(phone.Value,row.Body!,payload.ButtonText,payload.MetaFlowId,payload.Token,payload.StartScreen,account.Value,payload.Catalogs,initial.Value);
    }

    private Result<IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>>> InitialValues(WhatsAppFormDispatchRow row,
        WhatsAppFlowSessionPayload payload,WhatsAppFlowAssistantContent? content)
    {
        if(row.ProtectedProjection is null)
            return content is null ? Result.Success<IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>>>(new Dictionary<string,IReadOnlyDictionary<string,string>>()) : FlowErrors.StateConflict;
        WhatsAppFlowProjection projection;
        try { projection=projections.ReadProjection(row.BindingOperationId,row.BindingId,row.VersionId,row.Culture,row.ProtectedProjection); }
        catch(Exception exception) when(exception is CryptographicException or InvalidOperationException) { return FlowErrors.StateConflict; }
        if(projection.Hash != payload.ExpectedHash || projection.Culture != row.Culture) return FlowErrors.StateConflict;
        var values=new Dictionary<string,string>(StringComparer.Ordinal);
        if(content is not null)
        {
            var origin=content.Origin;
            if(origin.ProposedValues.Select(field=>field.FieldId).Distinct().Count()!=origin.ProposedValues.Count
                || origin.IncludedFieldIds.Distinct().Count()!=origin.IncludedFieldIds.Count) return FlowErrors.StateConflict;
            foreach(var fieldId in origin.IncludedFieldIds)
            {
                var proposed=origin.ProposedValues.SingleOrDefault(field=>field.FieldId==fieldId);
                var mapped=projection.Mapping.SingleOrDefault(field=>field.FieldId==fieldId);
                if(proposed is null || mapped is null || mapped.Type!=proposed.Type || mapped.Files is not null) return FlowErrors.StateConflict;
                values.Add(mapped.Key,proposed.Value);
            }
        }
        return WhatsAppFlowPrefillPolicy.Build(projection,values);
    }
}
