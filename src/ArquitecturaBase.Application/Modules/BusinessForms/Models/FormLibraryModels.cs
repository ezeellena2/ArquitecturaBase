namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

/// <summary>A standard of the library (BI1-A, as DOC1): its name and description in the display language, how many forms came from it,
/// the language a copy is written in (its name goes under it when adding) and the module that brings it, when it says.</summary>
public sealed record FormLibraryStandardResponse(string Key, int Version, string Name, string Description, string Prefix, int Copies, string Culture,
    string SuggestedPrefix)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }
    public override string ToString() => "FormLibraryStandardResponse { Content = [REDACTED] }";
}
public sealed record FormLibraryResponse(IReadOnlyList<FormLibraryStandardResponse> Standards);

/// <summary>«Agregar»: the new form's name and prefix, under its own request id so that a repeat gets the same draft.</summary>
public sealed record AddFormStandardRequest(Guid RequestId, IReadOnlyDictionary<string, string> Name, string Prefix, Guid? FolderId = null)
{
    public override string ToString() => "AddFormStandardRequest { Content = [REDACTED] }";
}

/// <summary>The standard a draft is created from; set only by «Agregar», never from HTTP. Bindings go by local id.</summary>
public sealed record FormDraftStandardOrigin(string Key, int Version, IReadOnlyDictionary<string, string> Bindings)
{
    /// <summary>
    /// What the copy leaves in the same save once it has the new definition and the identities it gave: the catalogs the standard
    /// brings with their first options, its rules and the folder (<c>FormStandardCopyRecorder</c>). The draft's save only runs it.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Func<ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormDefinition, FormIdentityMap, CancellationToken, Task<ArquitecturaBase.Domain.Results.Result>>? RecordAsync { get; init; }
    public override string ToString() => "FormDraftStandardOrigin { Content = [REDACTED] }";
}
/// <summary>A catalog a standard names, by Key: whether it is active.</summary>
public sealed record FormLibraryCatalogRow(Guid Id, bool IsActive);

/// <summary>Where a form came from (BI1-A): its standard, the version and the bound questions, by datum key and field id.</summary>
public sealed record FormDefinitionStandardResponse(string Key, int Version, IReadOnlyDictionary<string, Guid> Bound)
{
    public override string ToString() => "FormDefinitionStandardResponse { Content = [REDACTED] }";
}
