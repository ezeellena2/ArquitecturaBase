using System.Reflection;

namespace ArquitecturaBase.ArchitectureTests.Support;

/// <summary>
/// Los puntos de entrada de un caso de uso: las clases de Application/Services que implementan un contrato de
/// Interfaces/Services, o un puerto productivo neutral declarado por la contribución de su módulo. Solo ellos abren
/// el límite transaccional (TransactionBoundaryTests) y escriben la cookie de la
/// aplicación, después de confirmarlo (IdentityBoundaryTests).
/// </summary>
internal static class UseCaseEntryPoints
{
    private const string ServicesNamespace = "ArquitecturaBase.Application.Services";
    private const string ServiceInterfacesNamespace = "ArquitecturaBase.Application.Interfaces.Services";

    public static bool Contains(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type.ResidesIn(ServicesNamespace)
            && (type.GetInterfaces().Any(contract => contract.ResidesIn(ServiceInterfacesNamespace))
                || type.FullName is { } name && ModuleTestContributions.All.SelectMany(module => module.AdditionalUseCaseEntryPoints)
                    .Contains(name, StringComparer.Ordinal));
    }

    /// <summary>Por nombre, como lo da el IL: el tipo se busca en <paramref name="assemblies"/>.</summary>
    public static bool Contains(IEnumerable<Assembly> assemblies, string typeName) =>
        assemblies.Select(assembly => assembly.GetType(typeName)).OfType<Type>().FirstOrDefault() is { } type
        && Contains(type);
}
