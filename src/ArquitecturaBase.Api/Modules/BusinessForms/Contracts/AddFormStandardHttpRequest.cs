using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

// BI1-A: «Agregar» from the library, with the new form's name and prefix under its own request id, and the folder it goes to
// (without one, the standard's).
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddFormStandardHttpRequest(Guid RequestId, IReadOnlyDictionary<string, string> Name, string Prefix, Guid? FolderId = null)
{
    public override string ToString() => "AddFormStandardHttpRequest { Content = [REDACTED] }";
}
