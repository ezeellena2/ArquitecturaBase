using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Security;
internal sealed class ArcaKeyRing(IOptions<ArcaSecretProtectionOptions> options)
{
    public string ActiveKeyId => options.Value.ActiveKeyId;
    public string InstallationId => options.Value.InstallationId;
    public byte[]? Find(string keyId) => options.Value.Keys.TryGetValue(keyId, out var value) ? Convert.FromBase64String(value) : null;
}
