using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// BI1-A: the menu shows «Biblioteca» only when an installed module brings standards, so the client hears it as `form-library`.
public sealed class FormLibraryCapabilityTests
{
    [Fact]
    public void The_client_hears_of_the_library_only_when_a_module_brings_standards()
    {
        Assert.DoesNotContain("form-library", Features(withSource: false));
        Assert.Contains("form-library", Features(withSource: true));
    }

    private static IReadOnlyCollection<string> Features(bool withSource)
    {
        var services = new ServiceCollection();
        if (withSource) services.AddSingleton<IStructuredFormStandardSource, EmptySource>();
        using var provider = services.BuildServiceProvider();
        return new FormClientCapabilitySource(new MemoryPrivateFileStore(), provider.GetRequiredService<IServiceProviderIsService>()).InstalledFeatures;
    }

    private sealed class EmptySource : IStructuredFormStandardSource
    {
        public IReadOnlyList<StructuredFormStandard> Standards => [];
    }
}
