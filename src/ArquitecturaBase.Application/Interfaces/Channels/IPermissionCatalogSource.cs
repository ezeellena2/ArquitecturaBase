namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>Permission definitions contributed by an optional module; texts follow the current culture.</summary>
public interface IPermissionCatalogSource
{
    IReadOnlyCollection<string> Codes { get; }
    string Area(string area);
    string Name(string code);
    string Description(string code);
}
