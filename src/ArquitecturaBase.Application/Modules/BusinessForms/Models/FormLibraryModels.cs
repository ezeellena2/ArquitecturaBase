namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

/// <summary>A standard of the library (BI1-A, as DOC1): its name and description in the display language and how many forms came from it.</summary>
public sealed record FormLibraryStandardResponse(string Key, int Version, string Name, string Description, string Prefix, int Copies)
{
    public override string ToString() => "FormLibraryStandardResponse { Content = [REDACTED] }";
}
public sealed record FormLibraryResponse(IReadOnlyList<FormLibraryStandardResponse> Standards);

/// <summary>«Agregar»: the new form's name and prefix, under its own request id so that a repeat gets the same draft.</summary>
public sealed record AddFormStandardRequest(Guid RequestId, IReadOnlyDictionary<string, string> Name, string Prefix)
{
    public override string ToString() => "AddFormStandardRequest { Content = [REDACTED] }";
}

/// <summary>The standard a draft is created from; set only by «Agregar», never from HTTP. Bindings go by local id.</summary>
public sealed record FormDraftStandardOrigin(string Key, int Version, IReadOnlyDictionary<string, string> Bindings)
{
    /// <summary>The catalogs the standard brings that do not exist yet: created in the same save as the draft.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormCatalog> NewCatalogs { get; init; } = [];
    public override string ToString() => "FormDraftStandardOrigin { Content = [REDACTED] }";
}
/// <summary>A catalog a standard names, by Key: whether it is active.</summary>
public sealed record FormLibraryCatalogRow(Guid Id, bool IsActive);

/// <summary>Where a form came from (BI1-A): its standard, the version and the bound questions, by datum key and field id.</summary>
public sealed record FormDefinitionStandardResponse(string Key, int Version, IReadOnlyDictionary<string, Guid> Bound)
{
    public override string ToString() => "FormDefinitionStandardResponse { Content = [REDACTED] }";
}
