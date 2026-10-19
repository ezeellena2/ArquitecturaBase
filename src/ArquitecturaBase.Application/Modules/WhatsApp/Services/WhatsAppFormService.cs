using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Logging;
using Microsoft.Extensions.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFormService(IWhatsAppFormRecipientReader reader, IStructuredFormSource source,
    IPermissionService permissions, ICurrentUser currentUser, IRequestValidator validator, ILogger<WhatsAppFormService> logger) : IWhatsAppFormService
{
    private Guid Actor => currentUser.UserId ?? throw new InvalidOperationException("The form selector requires an authenticated operator.");
    public Task<Result<WhatsAppFormPreparationResponse>> PrepareAsync(Guid contactId, Guid definitionId, Guid versionId, CancellationToken ct) =>
        OperationLog.RunAsync<Result<WhatsAppFormPreparationResponse>>(logger, "PrepareWhatsAppForm",
            () => PrepareCoreAsync(contactId, definitionId, versionId, ct));

    private async Task<Result<WhatsAppFormPreparationResponse>> PrepareCoreAsync(Guid contactId, Guid definitionId, Guid versionId, CancellationToken ct)
    {
        var recipient = await reader.FindAsync(contactId, ct);
        if (recipient is null || !await permissions.HasPermissionAsync(Actor, WhatsAppPermissions.Read, ct)
            || !await permissions.HasPermissionAsync(Actor, WhatsAppPermissions.Send, ct)) return WhatsAppChatErrors.NotFound;
        if (recipient.UserId is not { } userId || !recipient.IsActive || recipient.Culture is not ("es" or "en"))
            return FlowErrors.RecipientContextChanged;
        if (await source.FindAsync(userId, recipient.Culture, definitionId, versionId, ct) is null) return FlowErrors.FormUnavailable;
        var preparation = await source.PrepareAsync(userId, recipient.Culture, definitionId, versionId, false, ct);
        return new WhatsAppFormPreparationResponse(preparation.Culture ?? recipient.Culture, WhatsAppFormRecipientRevision.Create(Actor, recipient), preparation);
    }
    public async Task<Result<WhatsAppFormsResponse>> ListAsync(ListWhatsAppFormsRequest request, CancellationToken cancellationToken)
    {
        if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
        var resolved = await ResolveAsync(request.ContactId, new() { Page = request.Page, PageSize = request.PageSize,
            Sort = request.Sort, Search = request.Search }, cancellationToken);
        return resolved is null ? WhatsAppChatErrors.NotFound : new WhatsAppFormsResponse(resolved.Page,
            resolved.Contact.FormEligibility?.RecipientCulture, resolved.Contact.FormEligibility?.RecipientContextRevision);
    }
    public async Task<WhatsAppFormContactResponse?> FindContactAsync(Guid contactId, CancellationToken cancellationToken) =>
        (await ResolveAsync(contactId, new() { PageSize = 1 }, cancellationToken))?.Contact;

    private async Task<ResolvedRecipient?> ResolveAsync(Guid contactId, ListStructuredFormsRequest request, CancellationToken ct)
    {
        var row = await reader.FindAsync(contactId, ct); if (row is null) return null;
        if (!await permissions.HasPermissionAsync(Actor, WhatsAppPermissions.Send, ct))
            return new(new(null, null), new PagedResult<StructuredFormSelectionRow>([], request.Page, request.PageSize, 0));
        var email = row.UserId.HasValue && await permissions.HasPermissionAsync(Actor, Permissions.Users.Read, ct)
            ? row.Email : null;
        var linked = row.UserId is { } userId ? new WhatsAppLinkedAccountResponse(userId, row.DisplayName ?? "", email, row.IsActive) : null;
        var culture = row.UserId.HasValue && row.Culture is "es" or "en" ? row.Culture : null;
        var page = row.UserId.HasValue && row.IsActive && culture is not null
            ? await source.ListAsync(row.UserId.Value, culture, request, ct)
            : new PagedResult<StructuredFormSelectionRow>([], request.Page, request.PageSize, 0);
        var availableCount = page.TotalCount;
        if (availableCount == 0 && row.UserId.HasValue && row.IsActive && culture is not null && !string.IsNullOrWhiteSpace(request.Search))
            availableCount = (await source.ListAsync(row.UserId.Value, culture, new() { PageSize = 1 }, ct)).TotalCount;
        var status = linked is null ? "NoLinkedAccount" : !row.IsActive ? "InactiveAccount" : availableCount == 0 ? "NoEligibleForms" : "Eligible";
        var revision = status == "Eligible" ? WhatsAppFormRecipientRevision.Create(Actor, row) : null;
        return new(new(linked, new(status, culture, revision)), page);
    }
    private sealed record ResolvedRecipient(WhatsAppFormContactResponse Contact, PagedResult<StructuredFormSelectionRow> Page);
}
