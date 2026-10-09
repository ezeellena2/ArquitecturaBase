using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Application.Channels;

internal sealed class PermissionCatalog : IPermissionCatalog
{
    private readonly Dictionary<string, IPermissionCatalogSource> sources = new(StringComparer.Ordinal);
    private readonly HashSet<string> codes;

    public PermissionCatalog(IEnumerable<IPermissionCatalogSource> extensions)
    {
        codes = new HashSet<string>(Permissions.All, StringComparer.Ordinal);
        var all = Permissions.All.ToList();
        foreach (var source in extensions)
        {
            foreach (var code in source.Codes)
            {
                if (!codes.Add(code))
                {
                    throw new InvalidOperationException("Permission catalog sources must declare unique codes.");
                }

                sources.Add(code, source);
                all.Add(code);
            }
        }

        All = all.AsReadOnly();
    }

    public IReadOnlyCollection<string> All { get; }
    public bool Contains(string code) => codes.Contains(code);
    public string Area(string area) => sources.FirstOrDefault(pair => pair.Key.StartsWith(area + ".", StringComparison.Ordinal)).Value
        is { } source ? source.Area(area) : PermissionTexts.Area(area);
    public string Name(string code) => sources.TryGetValue(code, out var source) ? source.Name(code) : PermissionTexts.Permission(code);
    public string Description(string code) => sources.TryGetValue(code, out var source) ? source.Description(code) : PermissionTexts.Description(code);
}
