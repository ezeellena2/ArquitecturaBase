using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

/// <summary>Wraps legacy plaintext master keys without changing key ids or invalidating existing cookies.</summary>
internal sealed class DataProtectionKeySeeder(ApplicationDbContext dbContext, IOptions<KeyManagementOptions> options)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var encryptor = options.Value.XmlEncryptor ?? throw new InvalidOperationException("DataProtection requires encryption at rest.");
        XNamespace protectionNamespace = "http://schemas.asp.net/2015/03/dataProtection";
        var keys = await dbContext.DataProtectionKeys.ToListAsync(cancellationToken);
        foreach (var key in keys)
        {
            if (key.Xml is null) continue;
            var xml = XElement.Parse(key.Xml);
            var secrets = xml.Descendants().Where(element => (bool?)element.Attribute(protectionNamespace + "requiresEncryption") == true).ToArray();
            foreach (var secret in secrets)
            {
                var encrypted = encryptor.Encrypt(new XElement(secret));
                secret.ReplaceWith(new XElement(protectionNamespace + "encryptedSecret", new XAttribute("decryptorType", encrypted.DecryptorType.AssemblyQualifiedName!), encrypted.EncryptedElement));
            }
            if (secrets.Length > 0) key.Xml = xml.ToString(SaveOptions.DisableFormatting);
        }
    }
}
