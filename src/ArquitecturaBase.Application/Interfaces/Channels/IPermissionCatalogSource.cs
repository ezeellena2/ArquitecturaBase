namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>Permission definitions contributed by an optional module; texts follow the current culture.</summary>
public interface IPermissionCatalogSource
{
    IReadOnlyCollection<string> Codes { get; }

    /// <summary>
    /// What each of its permissions includes (the module's <c>*Permissions.Includes</c>): only its own codes, and only
    /// codes with the same prefix, everything before the last dot (<c>ai.business.manage</c> includes only
    /// <c>ai.business.*</c>).
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> Includes { get; }
    string Area(string area);
    string Name(string code);
    string Description(string code);
}
