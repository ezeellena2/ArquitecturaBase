using System.Reflection;
using ArquitecturaBase.Application;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.TestSupport;

internal static class ModuleApplicationRegistration
{
    public static void AddAll(IServiceCollection services)
    {
        var registrations = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(x => x.IsPublic && x.IsAbstract && x.IsSealed
                && x.Namespace?.StartsWith("ArquitecturaBase.Application.Modules.", StringComparison.Ordinal) == true
                && x.Name.EndsWith("ApplicationRegistration", StringComparison.Ordinal))
            .OrderBy(x => x.FullName, StringComparer.Ordinal);
        foreach (var type in registrations)
        {
            var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Static),
                x => x.Name.StartsWith("Add", StringComparison.Ordinal) && x.Name.EndsWith("Application", StringComparison.Ordinal)
                    && x.GetParameters() is [{ ParameterType: var parameter }] && parameter == typeof(IServiceCollection));
            method.Invoke(null, [services]);
        }
    }
}
