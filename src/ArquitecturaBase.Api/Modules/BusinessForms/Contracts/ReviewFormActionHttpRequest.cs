using System.Text.Json.Serialization;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
// A notice is reviewed per person (RecipientUserId, and WasSent says whether it went out); an issuance has no recipient, and
// WasSent says whether to go on with what its reason allows or not issue it.
public sealed record ReviewFormActionHttpRequest(Guid? RecipientUserId,[property: JsonRequired] long ExpectedRevision,[property: JsonRequired] bool WasSent)
{
    public override string ToString() => "ReviewFormActionHttpRequest { Decision = [REDACTED] }";
}
