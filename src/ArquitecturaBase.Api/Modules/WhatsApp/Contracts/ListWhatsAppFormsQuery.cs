namespace ArquitecturaBase.Api.Modules.WhatsApp.Contracts;

public sealed record ListWhatsAppFormsQuery(int? Page = null, int? PageSize = null, string? Sort = null, string? Search = null)
{
    public override string ToString() => "ListWhatsAppFormsQuery { Search = [REDACTED] }";
}
