using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AcceptFormPublicationHttpRequest(Guid RequestId, long ExpectedRevision, string CheckToken,
    IReadOnlyDictionary<string, string> Name, string Prefix, FormDraftHttpRequest Draft)
{
    public override string ToString() => "AcceptFormPublicationHttpRequest { Content = [REDACTED] }";
}
