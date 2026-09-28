using System.Reflection;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;
using Microsoft.AspNetCore.Mvc.ApplicationParts;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// Registers only the shared /test controllers. Other public MVC probe controllers in this assembly are added
/// explicitly by their contract tests and must not enter the base factory's route inventory.
/// </summary>
internal sealed class TestControllerApplicationPart : ApplicationPart, IApplicationPartTypeProvider
{
    public override string Name => nameof(TestControllerApplicationPart);

    public IEnumerable<TypeInfo> Types =>
    [
        typeof(TestController).GetTypeInfo(),
        typeof(WidgetsTestController).GetTypeInfo(),
        typeof(LoginLinkTestController).GetTypeInfo(),
        typeof(ExternalLoginTestController).GetTypeInfo(),
    ];
}
