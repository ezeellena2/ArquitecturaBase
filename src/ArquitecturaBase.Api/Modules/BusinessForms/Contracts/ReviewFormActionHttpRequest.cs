using System.Text.Json.Serialization;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewFormActionHttpRequest([property: JsonRequired] long ExpectedRevision,[property: JsonRequired] bool WasSent)
{
    public override string ToString() => "ReviewFormActionHttpRequest { Decision = [REDACTED] }";
}
