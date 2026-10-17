using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApproveFormCatalogProposalHttpRequest(long? ExpectedRevision,
    IReadOnlyDictionary<string,string?>? Name, IReadOnlyDictionary<string,string?>? Description,
    FormCatalogFilterDefinitionHttpRequest? Filters, Guid? SampleUserId)
{
    public override string ToString() => "ApproveFormCatalogProposalHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DiscardFormCatalogProposalHttpRequest(long? ExpectedRevision);
