using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

// BI1-A: «Agregar» from the library, with the new form's name and prefix under its own request id.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddFormStandardHttpRequest(Guid RequestId, IReadOnlyDictionary<string, string> Name, string Prefix)
{
    public override string ToString() => "AddFormStandardHttpRequest { Content = [REDACTED] }";
}
