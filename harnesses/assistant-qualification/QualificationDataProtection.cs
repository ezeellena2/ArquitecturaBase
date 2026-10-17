using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AssistantQualification;

internal static class QualificationDataProtection
{
    public static void Configure(IServiceCollection services, QualificationSettings settings)
    {
        // This local Windows harness never falls back to a plaintext key ring.
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Qualification private keys require Windows DPAPI.");
        services.RemoveAll<IDataProtectionProvider>();
        services.AddDataProtection()
            .SetApplicationName("AssistantQualification:" + settings.RunId)
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(settings.OutputDirectory, "private-keys")))
            .ProtectKeysWithDpapi();
    }
}
