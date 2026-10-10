using System.Text.Json.Serialization;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CheckFormPublicationHttpRequest(long ExpectedRevision, IReadOnlyDictionary<string, string> Name, string Prefix, FormDraftHttpRequest Draft)
{
    public override string ToString() => "CheckFormPublicationHttpRequest { Content = [REDACTED] }";
}