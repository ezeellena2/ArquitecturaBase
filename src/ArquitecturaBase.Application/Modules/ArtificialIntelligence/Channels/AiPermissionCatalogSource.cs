using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;
internal sealed class AiPermissionCatalogSource : IPermissionCatalogSource
{
    internal static ResourceManager ResourceManager { get; } = new("ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources.Permissions", typeof(AiPermissionCatalogSource).Assembly);
    public IReadOnlyCollection<string> Codes => ArtificialIntelligencePermissions.All;
    public string Area(string area) => Text("Area." + area);
    public string Name(string code) => Text("Permission." + code);
    public string Description(string code) => Text("PermissionDescription." + code);
    private static string Text(string key) => ResourceManager.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new InvalidOperationException("The module permission text is missing.");
}
