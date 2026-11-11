namespace ArquitecturaBase.Application.Models.StructuredData;

/// <summary>
/// What a registry knows of a tax id: the name and the values of the catalog fields by their keys, in canonical form; or the stable
/// FailureCode of why it could not answer (an invalid tax id, one it does not know, the registry unavailable).
/// </summary>
public sealed record StructuredPartyLookupResponse(string? Name, IReadOnlyDictionary<string, string>? Fields, string? FailureCode = null)
{
    public override string ToString() => "StructuredPartyLookupResponse { Content = [REDACTED] }";
}
