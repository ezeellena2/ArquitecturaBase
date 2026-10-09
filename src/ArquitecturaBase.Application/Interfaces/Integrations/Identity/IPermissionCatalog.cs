namespace ArquitecturaBase.Application.Interfaces.Integrations.Identity;

public interface IPermissionCatalog
{
    IReadOnlyCollection<string> All { get; }
    bool Contains(string code);
    string Area(string area);
    string Name(string code);
    string Description(string code);
}
