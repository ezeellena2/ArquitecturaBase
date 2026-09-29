using System.Reflection;
using ArquitecturaBase.ArchitectureTests.Support;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// Dónde viven los errores: una clase <c>&lt;Entidad&gt;Errors</c> en <c>Domain/&lt;Área&gt;/</c>, aunque solo la use
/// Application (en un módulo, en <c>Domain/Modules/&lt;M&gt;/</c>: el módulo es el área), y nadie fuera de Domain arma un
/// <c>Error</c>: ni con las fábricas, ni con el constructor, que es público porque <c>Error</c> es un record, ni con una
/// copia <c>with</c>, que cambiaría su código. El prefijo del código no tiene que coincidir con la carpeta
/// (<c>Roles.Role</c> vive en <c>Authorization</c>). Los constructores de
/// <see cref="ValidationError"/> quedan permitidos: son errores por campo que arman <c>RequestValidator</c>,
/// <c>FieldErrors</c> y <c>ExternalLoginController</c> a partir de lo que devuelve un validador o el ModelState.
/// </summary>
public sealed class ErrorDeclarationTests
{
    private const string DomainNamespace = "ArquitecturaBase.Domain";

    // Carpetas de Domain que no son un área: los errores no viven ahí. Modules tampoco: cada módulo es su área.
    private static readonly string[] NonAreaFolders = ["Common", "Results", "ValueObjects", "Modules"];

    private static readonly string[] ErrorFactories =
        [nameof(Error.Failure), nameof(Error.Validation), nameof(Error.Unauthorized), nameof(Error.Forbidden),
         nameof(Error.NotFound), nameof(Error.Conflict), nameof(Error.TooManyRequests)];

    // El constructor de Error y la copia de un with: arman un Error igual que las fábricas. Los de ValidationError los
    // mira su propia regla, porque la llamada nombra a ValidationError, no a Error.
    private static readonly string[] ErrorConstructors = [".ctor", "<Clone>$"];

    private const string ValidationErrorType = "ArquitecturaBase.Domain.Results.ValidationError";

    private static readonly string[] FieldsConstructorOwners =
    [
        "ArquitecturaBase.Api.Controllers.ExternalLoginController",
        "ArquitecturaBase.Application.Common.Validation.FieldErrors",
        "ArquitecturaBase.Application.Common.Validation.RequestValidator",
    ];

    private const string CodedConstructorOwner = "ArquitecturaBase.Application.Common.Validation.FieldErrors";

    private static readonly Assembly Domain = typeof(Error).Assembly;

    private static readonly Assembly[] SourceAssemblies =
    [
        Domain,
        Assembly.Load("ArquitecturaBase.Application"),
        Assembly.Load("ArquitecturaBase.Infrastructure"),
        Assembly.Load("ArquitecturaBase.Api"),
    ];

    [Fact]
    public void Every_type_that_declares_an_error_is_a_public_static_Errors_class_in_a_Domain_area()
    {
        var declaring = SourceAssemblies.SelectMany(assembly => assembly.GetTypes()).Where(DeclaresError).ToArray();

        // Error.None es un miembro estático de tipo Error: se excluye Error por tipo, y este Assert evita que la
        // exclusión quede vieja.
        Assert.Contains(typeof(Error), declaring);
        Assert.Contains(declaring, type => type.Name == "UserErrors");

        var offenders = declaring
            .Where(type => type != typeof(Error))
            .SelectMany(type => Problems(type).Select(problem => $"{type.FullName}: {problem}"))
            .ToArray();

        Assert.True(offenders.Length == 0, "Errors live in a public static <Entity>Errors class in Domain/<Area>/: " + string.Join("; ", offenders));
    }

    [Fact]
    public void The_declaration_rule_reports_a_wrong_name_a_wrong_folder_and_a_class_that_is_not_static()
    {
        // Casos de control: sin ellos, un detector que no mira nada también pasaría.
        Assert.Empty(Problems(typeof(ArquitecturaBase.Domain.Users.ControlUserErrors)));
        Assert.Contains(Problems(typeof(ArquitecturaBase.Domain.Users.ControlUserRules)), problem => problem.Contains("name", StringComparison.Ordinal));
        Assert.Contains(Problems(typeof(ArquitecturaBase.Domain.Common.ControlCommonErrors)), problem => problem.Contains("area", StringComparison.Ordinal));
        Assert.Contains(Problems(typeof(ControlOutsideDomainErrors)), problem => problem.Contains("area", StringComparison.Ordinal));
        Assert.Contains(Problems(typeof(ArquitecturaBase.Domain.Users.ControlInstanceErrors)), problem => problem.Contains("static", StringComparison.Ordinal));
    }

    [Fact]
    public void In_a_module_the_area_is_the_module()
    {
        // Casos de control: los errores de un módulo van en la raíz de Domain/Modules/<M>/, no en una de las carpetas que
        // no son un área ni en la raíz de Modules.
        Assert.Empty(Problems(typeof(ArquitecturaBase.Domain.Modules.Control.ControlErrors)));
        Assert.Contains(Problems(typeof(ArquitecturaBase.Domain.Modules.Control.Common.ControlErrors)), problem => problem.Contains("area", StringComparison.Ordinal));
        Assert.Contains(Problems(typeof(ArquitecturaBase.Domain.Modules.ControlModulesRootErrors)), problem => problem.Contains("area", StringComparison.Ordinal));
    }

    [Fact]
    public void Nobody_outside_Domain_builds_an_error_with_the_factories()
    {
        var offenders = SourceAssemblies.Where(assembly => assembly != Domain)
            .SelectMany(FactoryCalls)
            .Select(call => $"{call.Owner} calls Error.{call.Method}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0, "Declare the error in a Domain <Entity>Errors class instead: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_factory_rule_finds_a_call_in_a_control_assembly()
    {
        // Caso de control: este ensamblado arma errores desde ControlOutsideDomainErrors con Error.Failure,
        // Error.Validation, el constructor y un with.
        var calls = FactoryCalls(typeof(ErrorDeclarationTests).Assembly).ToArray();

        Assert.Contains(calls, call => call.Owner.EndsWith(nameof(ControlOutsideDomainErrors), StringComparison.Ordinal) && call.Method == nameof(Error.Failure));
        Assert.Contains(calls, call => call.Owner.EndsWith(nameof(ControlOutsideDomainErrors), StringComparison.Ordinal) && call.Method == nameof(Error.Validation));
        Assert.Contains(calls, call => call.Owner.EndsWith(nameof(ControlOutsideDomainErrors), StringComparison.Ordinal) && call.Method == ".ctor");
        Assert.Contains(calls, call => call.Owner.EndsWith(nameof(ControlOutsideDomainErrors), StringComparison.Ordinal) && call.Method == "<Clone>$");
    }

    [Fact]
    public void Outside_Domain_only_the_known_owners_build_a_ValidationError()
    {
        var calls = SourceAssemblies.Where(assembly => assembly != Domain).SelectMany(CallSites.SizedCalls)
            .Where(call => call.DeclaringType == ValidationErrorType)
            .ToArray();

        // El conjunto exacto de dueños de cada forma: así también prueba que el detector ve las llamadas, y cualquier
        // otro dueño, o un with, lo rompe.
        Assert.Equal(FieldsConstructorOwners, OwnersOf(calls, ".ctor", parameters: 1));
        Assert.Equal([CodedConstructorOwner], OwnersOf(calls, ".ctor", parameters: 3));
        Assert.Empty(OwnersOf(calls, "<Clone>$", parameters: 0));
    }

    [Fact]
    public void The_ValidationError_rule_finds_the_constructors_and_the_copy_in_a_control_assembly()
    {
        // Casos de control: ControlOutsideDomainErrors arma un ValidationError con cada constructor y con un with.
        var calls = CallSites.SizedCalls(typeof(ErrorDeclarationTests).Assembly)
            .Where(call => call.DeclaringType == ValidationErrorType)
            .ToArray();

        Assert.Contains(typeof(ControlOutsideDomainErrors).FullName, OwnersOf(calls, ".ctor", parameters: 1));
        Assert.Contains(typeof(ControlOutsideDomainErrors).FullName, OwnersOf(calls, ".ctor", parameters: 3));
        Assert.Contains(typeof(ControlOutsideDomainErrors).FullName, OwnersOf(calls, "<Clone>$", parameters: 0));
    }

    private static string[] OwnersOf(IEnumerable<CallSites.SizedCall> calls, string method, int parameters) =>
    [
        .. calls
            .Where(call => call.Method == method && call.Parameters == parameters)
            .Select(call => call.Owner)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    // Los que declaran un campo o una propiedad estática de tipo Error (Error mismo, por Error.None).
    private static bool DeclaresError(Type type)
    {
        const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        return type.GetFields(Static).Any(field => typeof(Error).IsAssignableFrom(field.FieldType))
            || type.GetProperties(Static).Any(property => typeof(Error).IsAssignableFrom(property.PropertyType));
    }

    private static List<string> Problems(Type type)
    {
        var problems = new List<string>();

        if (!(type.IsPublic && type.IsAbstract && type.IsSealed))
        {
            problems.Add("it is not a public static class");
        }

        if (!type.Name.EndsWith("Errors", StringComparison.Ordinal))
        {
            problems.Add("its name does not end with Errors");
        }

        if (!IsInDomainArea(type))
        {
            problems.Add($"it is not in {DomainNamespace}.<area> (area folders exclude {string.Join(", ", NonAreaFolders)})");
        }

        return problems;
    }

    private static bool IsInDomainArea(Type type)
    {
        var prefix = DomainNamespace + ".";

        if (type.Namespace is not { } typeNamespace || !typeNamespace.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        // En un módulo, el área es el módulo (Domain.Modules.<M>), y debajo de él valen las mismas carpetas que no son un
        // área. Sin esto, el nombre canónico dejaría los errores del módulo en la raíz de Domain.
        if (ModuleNamespaces.ModuleOf(typeNamespace) is not null)
        {
            var insideModule = ModuleNamespaces.Canonical(typeNamespace);

            return insideModule == DomainNamespace
                || !NonAreaFolders.Contains(insideModule[prefix.Length..].Split('.')[0], StringComparer.Ordinal);
        }

        var area = typeNamespace[prefix.Length..].Split('.')[0];

        return !NonAreaFolders.Contains(area, StringComparer.Ordinal);
    }

    private static IEnumerable<CallSites.Call> FactoryCalls(Assembly assembly) =>
        CallSites.Calls(assembly).Where(call =>
            call.DeclaringType == typeof(Error).FullName
            && (ErrorFactories.Contains(call.Method, StringComparer.Ordinal)
                || ErrorConstructors.Contains(call.Method, StringComparer.Ordinal)));
}
