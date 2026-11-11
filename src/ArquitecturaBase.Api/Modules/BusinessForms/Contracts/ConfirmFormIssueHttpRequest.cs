using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

/// <summary>
/// «¿La emito?»: true issues it; false voids the response with «No se emitió». After a summary that failed, «Volver a probar» is
/// true with <c>retry</c>: the person saw the failure, and the summary is asked again.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmFormIssueHttpRequest([property: JsonRequired] bool Issue, bool Retry = false);
