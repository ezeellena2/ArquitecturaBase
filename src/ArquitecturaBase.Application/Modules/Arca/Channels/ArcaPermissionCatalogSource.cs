using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Modules.Arca;
namespace ArquitecturaBase.Application.Modules.Arca.Channels;
internal sealed class ArcaPermissionCatalogSource : IPermissionCatalogSource
{
    internal static ResourceManager ResourceManager { get; } = new("ArquitecturaBase.Application.Modules.Arca.Resources.Permissions", typeof(ArcaPermissionCatalogSource).Assembly);
    public IReadOnlyCollection<string> Codes => ArcaPermissions.All;
    public string Area(string area) => Text("Area." + area);
    public string Name(string code) => Text("Permission." + code);
    public string Description(string code) => Text("PermissionDescription." + code);
    private static string Text(string key) => ResourceManager.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new InvalidOperationException("The module permission text is missing.");
}
