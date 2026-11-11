using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

/// <summary>«¿La emito?»: true issues it; false voids the response with «No se emitió».</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmFormIssueHttpRequest([property: JsonRequired] bool Issue);
