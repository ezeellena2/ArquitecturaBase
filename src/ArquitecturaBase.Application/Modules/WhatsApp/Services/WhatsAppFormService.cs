using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFormService(IWhatsAppFormRecipientReader reader, IStructuredFormSource source,
    IPermissionService permissions, ICurrentUser currentUser, IRequestValidator validator) : IWhatsAppFormService
{
    private Guid Actor => currentUser.UserId ?? throw new InvalidOperationException("The form selector requires an authenticated operator.");
    public async Task<Result<WhatsAppFormsResponse>> ListAsync(ListWhatsAppFormsRequest request, CancellationToken cancellationToken)
    {
        if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
        var resolved = await ResolveAsync(request.ContactId, new() { Page = request.Page, PageSize = request.PageSize,
            Sort = request.Sort, Search = request.Search }, cancellationToken);
        return resolved is null ? WhatsAppChatErrors.NotFound : new WhatsAppFormsResponse(resolved.Page,
            resolved.Contact.FormEligibility.RecipientCulture, resolved.Contact.FormEligibility.RecipientContextRevision);
    }
    public async Task<WhatsAppFormContactResponse?> FindContactAsync(Guid contactId, CancellationToken cancellationToken) =>
        (await ResolveAsync(contactId, new() { PageSize = 1 }, cancellationToken))?.Contact;

    private async Task<ResolvedRecipient?> ResolveAsync(Guid contactId, ListStructuredFormsRequest request, CancellationToken ct)
    {
        var row = await reader.FindAsync(contactId, ct); if (row is null) return null;
        var linked = row.UserId is { } userId ? new WhatsAppLinkedAccountResponse(userId, row.DisplayName ?? "", row.Email, row.IsActive) : null;
        var culture = row.UserId.HasValue && row.Culture is "es" or "en" ? row.Culture : null;
        var page = row.UserId.HasValue && row.IsActive && culture is not null
            ? await source.ListAsync(row.UserId.Value, culture, request, ct)
            : new PagedResult<StructuredFormSelectionRow>([], request.Page, request.PageSize, 0);
        var availableCount = page.TotalCount;
        if (availableCount == 0 && row.UserId.HasValue && row.IsActive && culture is not null && !string.IsNullOrWhiteSpace(request.Search))
            availableCount = (await source.ListAsync(row.UserId.Value, culture, new() { PageSize = 1 }, ct)).TotalCount;
        var status = linked is null ? "NoLinkedAccount" : !row.IsActive ? "InactiveAccount" : availableCount == 0 ? "NoEligibleForms" : "Eligible";
        var revision = status == "Eligible" && await permissions.HasPermissionAsync(Actor, WhatsAppPermissions.Send, ct)
            ? WhatsAppFormRecipientRevision.Create(Actor, row) : null;
        return new(new(linked, new(status, culture, revision)), page);
    }
    private sealed record ResolvedRecipient(WhatsAppFormContactResponse Contact, PagedResult<StructuredFormSelectionRow> Page);
}
