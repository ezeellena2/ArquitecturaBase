using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AssistantQualification;

internal static class DataProtectionOfflineChecks
{
    public static void Private_keys_recover_protected_fixtures_after_a_new_host()
    {
        InTemporaryDirectory(directory =>
        {
            var settings = Settings(directory, "offline_recovery");
            string ciphertext;
            using (var first = Build(settings)) ciphertext = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("public-fixture").Protect("fictitious fixture");
            using var second = Build(settings);
            string? restored = null;
            try { restored = second.GetRequiredService<IDataProtectionProvider>().CreateProtector("public-fixture").Unprotect(ciphertext); }
            catch (CryptographicException) { }
            Require(restored == "fictitious fixture");
            var files = Directory.GetFiles(Path.Combine(directory, "private-keys"), "*.xml");
            Require(files.Length > 0 && files.All(file => File.ReadAllText(file).Contains("encryptedSecret", StringComparison.Ordinal)));
        });
    }

    public static void Private_keys_do_not_lend_a_protector_to_another_run()
    {
        InTemporaryDirectory(directory =>
        {
            string ciphertext;
            using (var first = Build(Settings(directory, "offline_first"))) ciphertext = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("public-fixture").Protect("fictitious fixture");
            using var second = Build(Settings(directory, "offline_other"));
            var rejected = false;
            try { _ = second.GetRequiredService<IDataProtectionProvider>().CreateProtector("public-fixture").Unprotect(ciphertext); }
            catch (CryptographicException) { rejected = true; }
            Require(rejected);
        });
    }

    private static ServiceProvider Build(QualificationSettings settings)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        // A fresh host cannot recover a previous host's ephemeral test key ring.
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        QualificationDataProtection.Configure(services, settings);
        return services.BuildServiceProvider();
    }
    private static QualificationSettings Settings(string directory, string runId) => new(runId, directory, directory, "unused", "unused", "unused");
    private static void InTemporaryDirectory(Action<string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "assistant-qualification-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { check(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private static void Require(bool valid) { if (!valid) throw new InvalidOperationException("Offline check failed."); }
}
