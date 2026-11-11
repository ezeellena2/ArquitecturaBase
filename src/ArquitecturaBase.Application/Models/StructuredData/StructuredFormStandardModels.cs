namespace ArquitecturaBase.Application.Models.StructuredData;

// BI1-A: a standard of the library, brought by a module as data. DraftJson is the form in the forms module's draft format
// (format 1: culture, submit label, fields and sections), with UUID local ids so that calculations, conditions and filters name
// each other. Bindings gives, for each bound question's local id, the key of the datum the module needs; Catalogs, for each catalog
// question's local id, the stable Key of its catalog (a condition on that question names its options as «code:<code>», since the
// identities of the options are those of each installation); Folder, the system folder where a copy and the catalogs it creates go;
// Source, by language, the name of the module that brings it, which the library shows («ARCA»); Defaults, for a bound question of
// choosing one from a catalog, by its local id, the code of the option a new answer starts with (IV1-A: «Alícuota» in 21 %). A bound
// question of the draft may also declare toggles (IV2-A), yes-or-no settings with their default value that the issuer receives.
public sealed record StructuredFormStandard(string Key, int Version, IReadOnlyDictionary<string, string> Name,
    IReadOnlyDictionary<string, string> Description, string Prefix, string DraftJson, IReadOnlyDictionary<string, string> Bindings,
    IReadOnlyDictionary<string, string>? Catalogs = null, StructuredFormStandardFolder? Folder = null, IReadOnlyList<StructuredFormStandardCatalog>? OwnCatalogs = null,
    IReadOnlyDictionary<string, string>? Source = null, IReadOnlyDictionary<string, string>? Defaults = null)
{
    public override string ToString() => "StructuredFormStandard { Content = [REDACTED] }";
}
// The folder a standard files its copies in, found again by its Key after a rename or a move; Name, by language, is the one it gets
// when it is created (in the language of whoever adds the copy).
public sealed record StructuredFormStandardFolder(string Key, IReadOnlyDictionary<string, string> Name);
// A catalog the standard brings (a Manual one people edit, like «Clientes»): created with its fields and its first options, in their
// order, when the standard is added and no catalog has its Key yet; an existing one keeps its options. A field of type «catalog»
// names its source catalog by Key.
public sealed record StructuredFormStandardCatalog(string Key, string OriginalCulture, IReadOnlyDictionary<string, string> Name,
    IReadOnlyDictionary<string, string> Description, IReadOnlyList<StructuredFormStandardCatalogField> Fields,
    IReadOnlyList<StructuredFormStandardCatalogItem>? Items = null)
{
    public override string ToString() => "StructuredFormStandardCatalog { Content = [REDACTED] }";
}
// A first option of a catalog the standard brings («Consumidor final»), with its code and its text by language (the original's at least).
public sealed record StructuredFormStandardCatalogItem(string Code, IReadOnlyDictionary<string, string> Text)
{
    public override string ToString() => "StructuredFormStandardCatalogItem { Content = [REDACTED] }";
}
public sealed record StructuredFormStandardCatalogField(string Key, IReadOnlyDictionary<string, string> Name, string Type,
    string? SourceCatalog = null, int? MaxLength = null)
{
    public override string ToString() => "StructuredFormStandardCatalogField { Content = [REDACTED] }";
}
