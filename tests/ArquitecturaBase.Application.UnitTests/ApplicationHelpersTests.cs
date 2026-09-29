using System.Reflection;
using System.Reflection.Emit;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests;

/// <summary>
/// Convención de helpers (Etapa 3, tarea 9; plan maestro, Etapa 3, tarea 4 «Convención de helpers»; backend.md "Helpers de Application.Services").
/// Un helper es una pieza interna de un área que no implementa ninguna interfaz de <c>Interfaces.Services</c> y se
/// registra por su tipo concreto en <see cref="DependencyInjection.AddApplication"/> o
/// <see cref="DependencyInjection.AddWhatsAppWebhookApplicationServices"/>. Tiene que ser <c>internal sealed</c>,
/// vivir en <c>Services/&lt;Área&gt;</c> (en un módulo opcional, en <c>Modules/&lt;M&gt;/Services</c>: el módulo es el
/// área) y terminar con uno de los siete sufijos de la tabla (Policy, Guard, Issuer, Verifier, Linker, Revoker,
/// Recorder). El filtro deja afuera por construcción a los validadores de FluentValidation, a <c>RequestValidator</c>
/// (vive en <c>Common/Validation</c>), a las opciones y a los tipos que se crean con <c>new</c> o son estáticos
/// (<c>BotReply</c>, <c>IssuedLoginCode</c>, <c>IssuedLoginLink</c>, <c>InvitationFields</c>, <c>UserCultures</c>):
/// ninguno de ellos queda registrado como servicio o helper. Los helpers de un módulo entran con el gancho
/// <c>AddModules</c>, que implementa la parte de esta clase en la carpeta del módulo.
/// </summary>
public sealed partial class ApplicationHelpersTests
{
    private const string ServicesNamespace = "ArquitecturaBase.Application.Services";
    private const string ServiceInterfacesNamespace = "ArquitecturaBase.Application.Interfaces.Services";

    /// <summary>Los siete sufijos de la tabla, cada uno por su rol: decide, protege, emite, verifica, vincula, revoca, registra.</summary>
    private static readonly string[] TableSuffixes =
        ["Policy", "Guard", "Issuer", "Verifier", "Linker", "Revoker", "Recorder"];

    [Fact]
    public void The_helper_filter_sees_a_known_helper()
    {
        var helpers = Helpers();

        Assert.Contains(typeof(AccountAccessRevoker), helpers);
    }

    [Fact]
    public void Every_helper_is_internal_sealed_lives_in_an_area_folder_and_ends_with_a_table_suffix()
    {
        var helpers = Helpers();

        Assert.NotEmpty(helpers);
        Assert.All(helpers, helper => Assert.True(
            IsCompliantHelper(helper),
            $"{helper.Name} debería ser internal sealed, vivir en Services/<Área> (en un módulo, en Modules/<M>/Services) y terminar con uno de los sufijos de la tabla ({string.Join(", ", TableSuffixes)})."));
    }

    /// <summary>
    /// Caso de control: un tipo armado en memoria (como <c>TransactionBoundaryTests.TypeReceiving</c>), internal
    /// sealed y en <c>Services/Auth</c>, pero con un sufijo fuera de la tabla, tiene que fallar la comprobación:
    /// prueba que el detector de verdad mira el sufijo, y no solo que exista algún helper conforme.
    /// </summary>
    [Fact]
    public void A_helper_with_a_suffix_outside_the_table_is_detected()
    {
        var outsideTheTable = TypeNamed("ArquitecturaBase.Application.Services.Auth", "LoginAttemptManager");

        Assert.True(outsideTheTable.IsSealed);
        Assert.True(outsideTheTable.IsNotPublic);
        Assert.True(IsUnderNamespace(outsideTheTable, ServicesNamespace));
        Assert.False(IsCompliantHelper(outsideTheTable));
    }

    /// <summary>
    /// Casos de control: un tipo público, y uno en la raíz de <c>Services</c> (sin carpeta de área), los dos internal
    /// sealed salvo por eso y con un sufijo de la tabla, tienen que fallar la comprobación: prueba que el detector mira
    /// la visibilidad y la carpeta, no solo el sufijo.
    /// </summary>
    [Fact]
    public void A_public_helper_and_one_in_the_services_root_are_detected()
    {
        var publicHelper = TypeNamed("ArquitecturaBase.Application.Services.Auth", "LoginAttemptGuard", TypeAttributes.Public);
        var rootHelper = TypeNamed(ServicesNamespace, "LoginAttemptGuard");

        Assert.True(publicHelper.IsPublic);
        Assert.False(IsCompliantHelper(publicHelper));
        Assert.True(rootHelper.IsNotPublic);
        Assert.False(IsCompliantHelper(rootHelper));

        // Y el mismo tipo, en regla, pasa: los dos casos fallan por una sola razón.
        Assert.True(IsCompliantHelper(TypeNamed("ArquitecturaBase.Application.Services.Auth", "LoginAttemptGuard")));
    }

    /// <summary>
    /// Casos de control con un módulo inventado: en un módulo, <c>Modules/&lt;M&gt;/Services</c> es la carpeta del área
    /// (el módulo es el área), así que un helper ahí pasa, y uno en la raíz del módulo, sin <c>Services</c>, no.
    /// </summary>
    [Fact]
    public void In_a_module_the_helpers_live_in_its_services_folder()
    {
        Assert.True(IsCompliantHelper(TypeNamed("ArquitecturaBase.Application.Modules.Control.Services", "LoginAttemptGuard")));
        Assert.False(IsCompliantHelper(TypeNamed("ArquitecturaBase.Application.Modules.Control", "LoginAttemptGuard")));
    }

    /// <summary>
    /// Los descriptores de las dos registraciones de Application y las de los módulos cuyo <c>ImplementationType</c> es
    /// del ensamblado de Application, vive en <c>Application.Services</c> (o en el de un módulo) y no implementa ninguna
    /// interfaz de <c>Interfaces.Services</c> (tampoco la de un módulo).
    /// </summary>
    private static Type[] Helpers()
    {
        var services = new ServiceCollection();
        services.AddApplication().AddWhatsAppWebhookApplicationServices();
        AddModules(services);

        var applicationAssembly = typeof(DependencyInjection).Assembly;
        var serviceContracts = applicationAssembly.GetTypes()
            .Where(type => type.IsInterface && IsUnderNamespace(type, ServiceInterfacesNamespace))
            .ToHashSet();

        return services
            .Select(descriptor => descriptor.ImplementationType)
            .OfType<Type>()
            .Where(type => type.Assembly == applicationAssembly)
            .Where(type => IsUnderNamespace(type, ServicesNamespace))
            .Where(type => !type.GetInterfaces().Any(serviceContracts.Contains))
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// Registra lo de cada módulo, como Program.cs. Lo implementa la parte de esta clase en la carpeta del módulo.
    /// </summary>
    static partial void AddModules(IServiceCollection services);

    private static bool IsCompliantHelper(Type type) =>
        type.IsSealed
        && type.IsNotPublic
        && LivesInAnAreaFolder(type)
        && TableSuffixes.Any(suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal));

    /// <summary>
    /// En <c>Services/&lt;Área&gt;</c> o, en un módulo, en <c>Modules/&lt;M&gt;/Services</c>. El nombre canónico del
    /// módulo (<see cref="ModuleNamespaces.Canonical"/>) sería la raíz de <c>Services</c>, que no es un área.
    /// </summary>
    private static bool LivesInAnAreaFolder(Type type) =>
        type.Namespace is { } typeNamespace
        && ModuleNamespaces.Canonical(typeNamespace) is var canonical
        && (canonical != typeNamespace
            ? IsUnderNamespace(canonical, ServicesNamespace)
            : IsUnderNamespace(typeNamespace, ServicesNamespace) && typeNamespace != ServicesNamespace);

    /// <summary>Si el tipo vive en el namespace o en uno de sus sub-namespaces, contando los de un módulo.</summary>
    private static bool IsUnderNamespace(Type type, string ns) =>
        type.Namespace is { } typeNamespace && IsUnderNamespace(ModuleNamespaces.Canonical(typeNamespace), ns);

    private static bool IsUnderNamespace(string typeNamespace, string ns) =>
        typeNamespace == ns || typeNamespace.StartsWith(ns + ".", StringComparison.Ordinal);

    /// <summary>
    /// Un tipo armado en memoria, sealed y por defecto internal, con el namespace y el nombre que se le pidan. No se registra ni
    /// se instancia nunca: alcanza con que la reflexión lo vea, igual que <c>TransactionBoundaryTests.TypeReceiving</c>.
    /// </summary>
    private static Type TypeNamed(string @namespace, string name, TypeAttributes visibility = TypeAttributes.NotPublic)
    {
        var assemblyName = new AssemblyName("ApplicationHelpersControl");
        var type = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndCollect)
            .DefineDynamicModule(assemblyName.Name!)
            .DefineType($"{@namespace}.{name}", visibility | TypeAttributes.Sealed);

        return type.CreateType();
    }
}
