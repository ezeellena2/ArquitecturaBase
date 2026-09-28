using System.Reflection;
using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class PermissionAuthorizationTests
{
    private static readonly Assembly ApiAssembly = typeof(HasPermissionAttribute).Assembly;

    // PermissionPolicyProvider es internal: el prefijo se lee de su constante para no repetir el texto acá.
    private static readonly string PermissionPolicyPrefix = (string)ApiAssembly
        .GetType("ArquitecturaBase.Api.Authorization.PermissionPolicyProvider", throwOnError: true)!
        .GetField("PolicyPrefix", BindingFlags.Public | BindingFlags.Static)!
        .GetRawConstantValue()!;

    [Fact]
    public void Controllers_ask_for_a_permission_with_HasPermission_instead_of_the_policy_name()
    {
        var authorizations = ControllerAuthorizations(Controllers()).ToArray();

        // Si la búsqueda dejara de encontrar los atributos, la regla de abajo pasaría en silencio.
        Assert.Contains(authorizations, entry => entry.Attribute is HasPermissionAttribute);

        var offenders = PolicyNameOffenders(authorizations).ToArray();

        Assert.True(offenders.Length == 0, "Use [HasPermission(...)] instead of: " + string.Join(", ", offenders));
    }

    [Fact]
    public void A_permission_policy_written_by_hand_is_reported_on_a_class_and_on_a_method()
    {
        // Caso de control: sin él, un detector que no mira nada también pasaría. El texto de la política va escrito en el
        // atributo (tiene que ser constante); si el prefijo cambiara, el control lo dice acá y no pasa en silencio.
        Assert.StartsWith(PermissionPolicyPrefix, PolicyByNameController.Policy, StringComparison.Ordinal);

        var offenders = PolicyNameOffenders(
            ControllerAuthorizations([typeof(PolicyByNameController), typeof(PolicyByNameOnMethodController)])).ToArray();

        Assert.Contains(offenders, entry => entry.StartsWith(typeof(PolicyByNameController).FullName + " ", StringComparison.Ordinal));
        Assert.Contains(offenders, entry => entry.Contains("PolicyByNameOnMethodController.Get ", StringComparison.Ordinal));
        Assert.Equal(2, offenders.Length);
    }

    [Fact]
    public void Controllers_do_not_ask_for_roles()
    {
        // Las rutas piden permisos, nunca roles: un rol en la ruta deja afuera a otro rol que tenga el mismo permiso, y
        // cambiar los permisos de un rol desde la administración no cambiaría nada.
        var authorizations = ControllerAuthorizations(Controllers()).ToArray();

        // Si la búsqueda dejara de encontrar los atributos, la regla de abajo pasaría en silencio.
        Assert.NotEmpty(authorizations);

        var offenders = RoleOffenders(authorizations).ToArray();

        Assert.True(offenders.Length == 0, "Use [HasPermission(...)] instead of: " + string.Join(", ", offenders));
    }

    [Fact]
    public void A_role_requirement_is_reported_on_a_class_and_on_a_method()
    {
        // Caso de control, también con [HasPermission] en otra acción del mismo controller, que no cuenta.
        var offenders = RoleOffenders(
            ControllerAuthorizations([typeof(RolesOnClassController), typeof(RolesOnMethodController)])).ToArray();

        Assert.Contains(offenders, entry => entry.StartsWith(typeof(RolesOnClassController).FullName + " ", StringComparison.Ordinal));
        Assert.Contains(offenders, entry => entry.Contains("RolesOnMethodController.Get ", StringComparison.Ordinal));
        Assert.Equal(2, offenders.Length);
    }

    private static IEnumerable<string> PolicyNameOffenders(
        IEnumerable<(string Location, AuthorizeAttribute Attribute)> authorizations) =>
        authorizations
            .Where(entry => entry.Attribute is not HasPermissionAttribute
                && entry.Attribute.Policy?.StartsWith(PermissionPolicyPrefix, StringComparison.Ordinal) == true)
            .Select(entry => $"{entry.Location} [Authorize(Policy = \"{entry.Attribute.Policy}\")]");

    private static IEnumerable<string> RoleOffenders(
        IEnumerable<(string Location, AuthorizeAttribute Attribute)> authorizations) =>
        authorizations
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Attribute.Roles))
            .Select(entry => $"{entry.Location} [Authorize(Roles = \"{entry.Attribute.Roles}\")]");

    [Fact]
    public void Every_required_permission_exists()
    {
        var required = RequiredPermissions(Controllers()).ToArray();

        // Si la búsqueda dejara de encontrar los atributos, la regla de abajo pasaría en silencio.
        Assert.NotEmpty(required);

        Assert.Empty(UnknownPermissions(required));
    }

    [Fact]
    public void A_misspelled_permission_is_reported_on_a_class_and_on_a_method()
    {
        // Caso de control: sin él, un detector que no mira nada también pasaría.
        var required = RequiredPermissions([typeof(MisspelledOnClassController), typeof(MisspelledOnMethodController)]).ToArray();

        var unknown = UnknownPermissions(required).ToArray();

        Assert.Contains(unknown, entry => entry.Contains("MisspelledOnClassController", StringComparison.Ordinal) && entry.Contains("users.raed", StringComparison.Ordinal));
        Assert.Contains(unknown, entry => entry.Contains("MisspelledOnMethodController.Get", StringComparison.Ordinal) && entry.Contains("roles.raed", StringComparison.Ordinal));
        Assert.Equal(2, unknown.Length);
    }

    private static IEnumerable<string> UnknownPermissions(IEnumerable<(string Location, string Permission)> required) =>
        required
            .Where(entry => !Permissions.All.Contains(entry.Permission, StringComparer.Ordinal))
            .Select(entry => $"{entry.Location} [HasPermission(\"{entry.Permission}\")]");

    private static IEnumerable<(string Location, string Permission)> RequiredPermissions(IEnumerable<Type> controllers)
    {
        foreach (var controller in controllers)
        {
            foreach (var attribute in controller.GetCustomAttributes<HasPermissionAttribute>(inherit: true))
            {
                yield return (controller.FullName!, attribute.Permission);
            }

            var actions = controller.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var action in actions)
            {
                foreach (var attribute in action.GetCustomAttributes<HasPermissionAttribute>(inherit: true))
                {
                    yield return ($"{controller.FullName}.{action.Name}", attribute.Permission);
                }
            }
        }
    }

    private static Type[] Controllers()
    {
        var controllers = ApiAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(type))
            .ToArray();

        Assert.NotEmpty(controllers);

        return controllers;
    }

    private static IEnumerable<(string Location, AuthorizeAttribute Attribute)> ControllerAuthorizations(
        IEnumerable<Type> controllers)
    {
        foreach (var controller in controllers)
        {
            foreach (var attribute in controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            {
                yield return (controller.FullName!, attribute);
            }

            var actions = controller.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var action in actions)
            {
                foreach (var attribute in action.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
                {
                    yield return ($"{controller.FullName}.{action.Name}", attribute);
                }
            }
        }
    }
}

[HasPermission("users.raed")]
file sealed class MisspelledOnClassController : ControllerBase;

file sealed class MisspelledOnMethodController : ControllerBase
{
    [HasPermission("roles.raed")]
    public IActionResult Get() => Ok();

    [HasPermission(Permissions.Users.Read)]
    public IActionResult Valid() => Ok();
}

[Authorize(Policy = Policy)]
file sealed class PolicyByNameController : ControllerBase
{
    public const string Policy = "permission:users.read";
}

file sealed class PolicyByNameOnMethodController : ControllerBase
{
    [Authorize(Policy = PolicyByNameController.Policy)]
    public IActionResult Get() => Ok();

    // Una política que no es de permisos no cuenta: por ejemplo, la de un esquema de autenticación.
    [Authorize(Policy = "other")]
    public IActionResult Other() => Ok();

    [HasPermission(Permissions.Users.Read)]
    public IActionResult Valid() => Ok();
}

[Authorize(Roles = SystemRoles.Admin)]
file sealed class RolesOnClassController : ControllerBase;

file sealed class RolesOnMethodController : ControllerBase
{
    [Authorize(Roles = SystemRoles.Admin)]
    public IActionResult Get() => Ok();

    [HasPermission(Permissions.Users.Read)]
    public IActionResult Valid() => Ok();

    [Authorize]
    public IActionResult SessionOnly() => Ok();
}
