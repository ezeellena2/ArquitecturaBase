using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Identity;

[Collection(ApiTestGroup.Name)]
public sealed class DataProtectionAtRestTests(ApiFactory factory)
{
    [Fact]
    public async Task Existing_plaintext_keys_are_wrapped_and_old_cookies_still_open()
    {
        using var rsa = RSA.Create(2048);
        var now = TimeProvider.System.GetUtcNow();
        var request = new CertificateRequest("CN=Legacy ring test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        var repository = new MemoryRepository();
        using var plaintext = Build(certificate, repository, encrypt: false);
        var cookie = plaintext.GetRequiredService<IDataProtectionProvider>().CreateProtector("legacy-cookie").Protect("old-session");
        var friendlyName = "legacy-test-" + Guid.NewGuid();
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
        {
            var db = provider.GetRequiredService<ApplicationDbContext>();
            db.DataProtectionKeys.Add(new DataProtectionKey { FriendlyName = friendlyName, Xml = repository.Elements.Single().ToString() });
            // Flush the test fixture using the same Identity-style context via the enclosing unit of work first.
            await Task.CompletedTask;
            return Result.Success();
        }, CommitPolicy.OnSuccess, TestContext.Current.CancellationToken));
        using var encrypted = Build(certificate, new MemoryRepository());
        await factory.ExecuteScopeAsync(provider => provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
        {
            await new DataProtectionKeySeeder(provider.GetRequiredService<ApplicationDbContext>(), encrypted.GetRequiredService<IOptions<KeyManagementOptions>>()).SeedAsync(ct);
            return Result.Success();
        }, CommitPolicy.OnSuccess, TestContext.Current.CancellationToken));
        var xml = await factory.ExecuteDbContextAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.DataProtectionKeys.Where(key => key.FriendlyName == friendlyName), TestContext.Current.CancellationToken));
        Assert.Contains("encryptedSecret", xml.Xml!, StringComparison.Ordinal);
        repository.Elements.Clear();
        repository.Elements.Add(XElement.Parse(xml.Xml!));
        using var restored = Build(certificate, repository);
        Assert.Equal("old-session", restored.GetRequiredService<IDataProtectionProvider>().CreateProtector("legacy-cookie").Unprotect(cookie));
    }
    [Fact]
    public void Encrypted_ring_is_readable_by_another_host_with_the_certificate()
    {
        using var rsa = RSA.Create(2048);
        var now = TimeProvider.System.GetUtcNow();
        var request = new CertificateRequest("CN=DataProtection tests", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        var repository = new MemoryRepository();
        using var first = Build(certificate, repository);
        var token = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie-test").Protect("private-cookie");
        Assert.Contains("encryptedSecret", repository.Elements.Single().ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("<value>", repository.Elements.Single().ToString(), StringComparison.Ordinal);
        using var second = Build(certificate, repository);
        Assert.Equal("private-cookie", second.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie-test").Unprotect(token));
    }

    private static ServiceProvider Build(X509Certificate2 certificate, IXmlRepository repository, bool encrypt = true)
    {
        var services = new ServiceCollection().AddLogging();
        var builder = services.AddDataProtection().SetApplicationName("at-rest-test");
        if (encrypt) builder.ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
        services.Configure<KeyManagementOptions>(options => options.XmlRepository = repository);
        return services.BuildServiceProvider();
    }

    private sealed class MemoryRepository : IXmlRepository
    {
        public List<XElement> Elements { get; } = [];
        public IReadOnlyCollection<XElement> GetAllElements() => Elements.Select(element => new XElement(element)).ToArray();
        public void StoreElement(XElement element, string? friendlyName) => Elements.Add(new XElement(element));
    }
}
