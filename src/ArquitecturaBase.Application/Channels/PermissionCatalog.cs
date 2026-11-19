using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Application.Channels;

/// <summary>
/// El catálogo compuesto: los permisos del núcleo y los de cada módulo presente, con lo que incluye cada uno. Al
/// componerse falla por un código repetido y por una inclusión que no es de la misma fuente, que no está en el catálogo o
/// que no tiene el mismo prefijo, todo lo que va antes del último punto: <c>ai.business.manage</c> sólo incluye
/// <c>ai.business.*</c>, y <c>ai.manage</c>, <c>ai.read</c> pero no <c>ai.business.read</c>.
/// </summary>
internal sealed class PermissionCatalog : IPermissionCatalog
{
    private readonly Dictionary<string, IPermissionCatalogSource> sources = new(StringComparer.Ordinal);
    private readonly HashSet<string> codes;
    private readonly Dictionary<string, IReadOnlyCollection<string>> includes = new(StringComparer.Ordinal);

    public PermissionCatalog(IEnumerable<IPermissionCatalogSource> extensions)
    {
        codes = new HashSet<string>(Permissions.All, StringComparer.Ordinal);
        var all = Permissions.All.ToList();
        List<(IReadOnlyCollection<string> Owned, IReadOnlyDictionary<string, IReadOnlyCollection<string>> Includes)> declared =
            [(Permissions.All, Permissions.Includes)];
        foreach (var source in extensions)
        {
            declared.Add((source.Codes, source.Includes));
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
        var direct = DirectInclusions(declared);
        foreach (var code in All)
        {
            includes.Add(code, Close(code, direct));
        }
    }

    public IReadOnlyCollection<string> All { get; }
    public bool Contains(string code) => codes.Contains(code);
    public IReadOnlyCollection<string> Includes(string code) => includes.TryGetValue(code, out var included) ? included : [];

    public IReadOnlySet<string> Effective(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in permissions.Where(codes.Contains))
        {
            allowed.Add(code);
            allowed.UnionWith(Includes(code));
        }

        return allowed;
    }

    public string Area(string area) => sources.FirstOrDefault(pair => pair.Key.StartsWith(area + ".", StringComparison.Ordinal)).Value
        is { } source ? source.Area(area) : PermissionTexts.Area(area);
    public string Name(string code) => sources.TryGetValue(code, out var source) ? source.Name(code) : PermissionTexts.Permission(code);
    public string Description(string code) => sources.TryGetValue(code, out var source) ? source.Description(code) : PermissionTexts.Description(code);

    // Cada fuente declara inclusiones sólo de sus códigos, hacia códigos del catálogo con el mismo prefijo.
    private Dictionary<string, IReadOnlyCollection<string>> DirectInclusions(
        IEnumerable<(IReadOnlyCollection<string> Owned, IReadOnlyDictionary<string, IReadOnlyCollection<string>> Includes)> declared)
    {
        var direct = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
        foreach (var (owned, inclusions) in declared)
        {
            foreach (var (code, included) in inclusions)
            {
                if (!owned.Contains(code, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException("Permission catalog sources must declare inclusions only for their own codes.");
                }

                if (included.Any(other => !codes.Contains(other) || !string.Equals(PrefixOf(other), PrefixOf(code), StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("A permission can only include catalog codes with its own prefix.");
                }

                direct.Add(code, included);
            }
        }

        return direct;
    }

    // Lo que incluye un código, también a través de lo que incluye, en el orden del catálogo.
    private IReadOnlyCollection<string> Close(string code, Dictionary<string, IReadOnlyCollection<string>> direct)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([code]);
        while (pending.TryPop(out var current))
        {
            foreach (var included in direct.TryGetValue(current, out var next) ? next : [])
            {
                if (!string.Equals(included, code, StringComparison.Ordinal) && reached.Add(included))
                {
                    pending.Push(included);
                }
            }
        }

        return [.. All.Where(reached.Contains)];
    }

    // Todo lo que va antes del último punto: el de ai.business.manage es ai.business.
    private static string PrefixOf(string code) =>
        code.LastIndexOf('.') is var dot and >= 0 ? code[..dot] : code;
}
