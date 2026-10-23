using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateFormPersonAttributeHttpRequest(string? Key, IReadOnlyDictionary<string, string?>? Name,
    string? Type, Guid? SourceCatalogId)
{
    public override string ToString() => "CreateFormPersonAttributeHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateFormPersonAttributeHttpRequest(long? ExpectedRevision, IReadOnlyDictionary<string, string?>? Name,
    string? Type, Guid? SourceCatalogId, int Position, bool IsActive, bool ConfirmValueRemoval = false)
{
    public override string ToString() => "UpdateFormPersonAttributeHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateFormPersonDataHttpRequest(long? ExpectedRevision, IReadOnlyDictionary<string, string?>? Values)
{
    public override string ToString() => "UpdateFormPersonDataHttpRequest { Content = [REDACTED] }";
}

public sealed record FormPersonDataQuery(int Page = 1, int PageSize = 20, string? Sort = null, string? Search = null)
{
    public override string ToString() => "FormPersonDataQuery { Search = [REDACTED] }";
}

public sealed record FormPersonAttributeOptionsQuery(int Page = 1, int PageSize = 20, string? Sort = null, string? Search = null)
{
    public override string ToString() => "FormPersonAttributeOptionsQuery { Search = [REDACTED] }";
}
