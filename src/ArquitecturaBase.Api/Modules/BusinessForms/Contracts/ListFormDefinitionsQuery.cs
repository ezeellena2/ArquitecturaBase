namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
public sealed record ListFormDefinitionsQuery(int Page = 1, int PageSize = 20, string? Search = null, string? Sort = null, string PublicationFilter = "All")
{ public override string ToString() => "ListFormDefinitionsQuery { Content = [REDACTED] }"; }
