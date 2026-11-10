namespace ArquitecturaBase.Application.Models.StructuredData;

// BI1-A: a standard of the library, brought by a module as data. DraftJson is the form in the forms module's draft format
// (format 1: culture, submit label, fields and sections), with UUID local ids so that calculations, conditions and filters name
// each other. Bindings gives, for each bound question's local id, the key of the datum the module needs; Catalogs, for each catalog
// question's local id, the stable Key of its catalog; Folder, the key of the system folder where a copy goes.
public sealed record StructuredFormStandard(string Key, int Version, IReadOnlyDictionary<string, string> Name,
    IReadOnlyDictionary<string, string> Description, string Prefix, string DraftJson, IReadOnlyDictionary<string, string> Bindings,
    IReadOnlyDictionary<string, string>? Catalogs = null, string? Folder = null, IReadOnlyList<StructuredFormStandardCatalog>? OwnCatalogs = null)
{
    public override string ToString() => "StructuredFormStandard { Content = [REDACTED] }";
}
// A catalog the standard brings (a Manual one people edit, like «Clientes»): created empty with its fields when the standard is
// added and no catalog has its Key yet. A field of type «catalog» names its source catalog by Key.
public sealed record StructuredFormStandardCatalog(string Key, string OriginalCulture, IReadOnlyDictionary<string, string> Name,
    IReadOnlyDictionary<string, string> Description, IReadOnlyList<StructuredFormStandardCatalogField> Fields)
{
    public override string ToString() => "StructuredFormStandardCatalog { Content = [REDACTED] }";
}
public sealed record StructuredFormStandardCatalogField(string Key, IReadOnlyDictionary<string, string> Name, string Type,
    string? SourceCatalog = null, int? MaxLength = null)
{
    public override string ToString() => "StructuredFormStandardCatalogField { Content = [REDACTED] }";
}
