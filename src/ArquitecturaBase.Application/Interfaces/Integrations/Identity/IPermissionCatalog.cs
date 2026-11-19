namespace ArquitecturaBase.Application.Interfaces.Integrations.Identity;

public interface IPermissionCatalog
{
    IReadOnlyCollection<string> All { get; }
    bool Contains(string code);

    /// <summary>
    /// What <paramref name="code"/> includes, directly or through what it includes, in catalog order: whoever holds it
    /// also holds these. Empty for a code that includes nothing or is not in the catalog.
    /// </summary>
    IReadOnlyCollection<string> Includes(string code);

    /// <summary>
    /// What these codes really allow: the ones in the catalog plus what each one includes. A code outside the catalog
    /// (the claim of a removed module) allows nothing.
    /// </summary>
    IReadOnlySet<string> Effective(IEnumerable<string> permissions);
    string Area(string area);
    string Name(string code);
    string Description(string code);
}
