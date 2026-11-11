namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

/// <summary>«Completar con ARCA» (CA1): the registry that completes this catalog, by language, and the field that holds the tax id.</summary>
public sealed record FormCatalogLookupResponse(IReadOnlyDictionary<string, string> Name, Guid FieldId);

public sealed record LookupFormCatalogPartyRequest(string? TaxId)
{
    public const int MaximumTaxIdLength = 20;
    public override string ToString() => "LookupFormCatalogPartyRequest { TaxId = [REDACTED] }";
}

/// <summary>
/// What the registry knows of a tax id, ready to fill an option of the catalog: its text and the value of each field it knows (a field
/// that names an option of another catalog, by the id of that option); or why it could not, with its code and its message.
/// </summary>
public sealed record FormCatalogLookupResultResponse(string? Text, IReadOnlyList<FormCatalogLookupValueResponse> Fields,
    string? FailureCode = null, string? FailureMessage = null)
{
    public override string ToString() => "FormCatalogLookupResultResponse { Content = [REDACTED] }";
}

public sealed record FormCatalogLookupValueResponse(Guid FieldId, string Value)
{
    public override string ToString() => "FormCatalogLookupValueResponse { Value = [REDACTED] }";
}
