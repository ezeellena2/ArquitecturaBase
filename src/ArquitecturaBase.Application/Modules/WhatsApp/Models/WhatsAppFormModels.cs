using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public sealed record ListWhatsAppFormsRequest(Guid ContactId) : PagedRequest
{
    public override string ToString() => "ListWhatsAppFormsRequest { Search = [REDACTED] }";
}
public sealed record WhatsAppLinkedAccountResponse(Guid UserId, string DisplayName, string? Email, bool IsActive);
public sealed record WhatsAppFormEligibilityResponse(string Status, string? RecipientCulture, string? RecipientContextRevision)
{
    public override string ToString() => $"WhatsAppFormEligibilityResponse {{ Status = {Status}, Context = [REDACTED] }}";
}
public sealed record WhatsAppFormContactResponse(WhatsAppLinkedAccountResponse? LinkedAccount, WhatsAppFormEligibilityResponse FormEligibility);
public sealed record WhatsAppFormsResponse(PagedResult<StructuredFormSelectionRow> Page, string? RecipientCulture, string? RecipientContextRevision)
{
    public override string ToString() => "WhatsAppFormsResponse { Presentation and Context = [REDACTED] }";
}
public sealed record WhatsAppFormRecipientRow(Guid ContactId, long LinkRevision, string? WaId, string? UserIdentifier,
    Guid? UserId, string? DisplayName, string? Email, bool IsActive, string? Culture, string? AccountStamp, string RoleFingerprint)
{
    public override string ToString() => "WhatsAppFormRecipientRow { Recipient = [REDACTED] }";
}
public sealed record WhatsAppThreadContactResponse : WhatsAppChatResponse
{
    public WhatsAppThreadContactResponse(WhatsAppChatResponse contact, WhatsAppFormContactResponse forms) : base(contact)
    {
        LinkedAccount = forms.LinkedAccount;
        FormEligibility = forms.FormEligibility;
    }
    public WhatsAppLinkedAccountResponse? LinkedAccount { get; }
    public WhatsAppFormEligibilityResponse FormEligibility { get; }
    public override string ToString() => "WhatsAppThreadContactResponse { Contact and Context = [REDACTED] }";
}
