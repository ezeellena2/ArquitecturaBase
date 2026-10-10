using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Protected_attempts_survive_restoring_the_encrypted_database_key_ring_and_reject_other_purposes()
    {
        using var admin = await AdminAsync();
        var keyDatabase = factory.NewDatabaseConnectionString("forms_keys"); var restoredDatabase = factory.NewDatabaseConnectionString("forms_keys_restore");
        using var rsa = RSA.Create(2048); var now = factory.Clock.GetUtcNow();
        var certificateRequest = new CertificateRequest("CN=Form recovery test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        await using var keys = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(keyDatabase).Options);
        await keys.Database.EnsureCreatedAsync(Ct);
        using var initial = BuildProtection(keyDatabase, certificate);
        await using var firstHost = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton(initial.GetRequiredService<IDataProtectionProvider>()))));
        using var first = firstHost.CreateClient(); first.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, admin.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var payload = Payload("Encrypted restoration proof"); var saved = await CreateAsync(first, payload);
        var requestId = saved.GetProperty("requestId").GetGuid();
        var snapshotKeys = await keys.DataProtectionKeys.ToListAsync(Ct); Assert.NotEmpty(snapshotKeys);
        Assert.All(snapshotKeys, key => Assert.Contains("encryptedSecret", key.Xml!, StringComparison.Ordinal));
        await using var restored = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(restoredDatabase).Options);
        await restored.Database.EnsureCreatedAsync(Ct);
        restored.DataProtectionKeys.AddRange(snapshotKeys.Select(key => new DataProtectionKey { FriendlyName = key.FriendlyName, Xml = key.Xml }));
        await restored.SaveChangesAsync(Ct);
        using var afterRestore = BuildProtection(restoredDatabase, certificate);
        await using var secondHost = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton(afterRestore.GetRequiredService<IDataProtectionProvider>()))));
        using var second = secondHost.CreateClient(); second.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, admin.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        using var recovery = await second.GetAsync("/api/forms/draft-changes/" + requestId, Ct); Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        Assert.Equal(saved.GetRawText(), (await recovery.ReadJsonAsync()).GetProperty("result").GetRawText());
        using var replay = await second.PostAsJsonAsync("/api/forms/definitions", payload, Ct); Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(saved.GetRawText(), (await replay.ReadJsonAsync()).GetRawText());
        var row = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDraftChange>().SingleAsync(x => x.RequestId == requestId, Ct));
        var context = new FormChangeProtectionContext(row.Id, row.OperatorUserId, row.RequestId, row.DefinitionId, row.Kind, row.ResultFormat);
        var codec = new DataProtectionFormChangeProtector(afterRestore.GetRequiredService<IDataProtectionProvider>());
        Assert.Throws<CryptographicException>(() => codec.Unprotect(context with { Actor = Guid.NewGuid() }, row.ProtectedResult!));
        Assert.Throws<CryptographicException>(() => afterRestore.GetRequiredService<IDataProtectionProvider>().CreateProtector("other-module").Unprotect(row.ProtectedResult!));
        Assert.DoesNotContain("Encrypted restoration proof", System.Text.Encoding.UTF8.GetString(row.ProtectedResult!), StringComparison.Ordinal);
        Assert.Equal(1, row.ResultFormat);
    }
    private static ServiceProvider BuildProtection(string connectionString, X509Certificate2 certificate)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDataProtection().SetApplicationName("ArquitecturaBase").PersistKeysToDbContext<ApplicationDbContext>()
            .ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
        return services.BuildServiceProvider();
    }
}
