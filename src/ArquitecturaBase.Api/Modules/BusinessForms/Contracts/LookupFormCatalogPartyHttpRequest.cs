using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

/// <summary>«Completar con ARCA»: the tax id written in the option, as the person typed it.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LookupFormCatalogPartyHttpRequest(string? TaxId)
{
    public override string ToString() => "LookupFormCatalogPartyHttpRequest { TaxId = [REDACTED] }";
}
