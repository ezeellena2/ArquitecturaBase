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
        var authorizations = ControllerAuthorizations().ToArray();

        // Si la búsqueda dejara de encontrar los atributos, la regla de abajo pasaría en silencio.
        Assert.Contains(authorizations, entry => entry.Attribute is HasPermissionAttribute);

        var offenders = authorizations
            .Where(entry => entry.Attribute is not HasPermissionAttribute
                && entry.Attribute.Policy?.StartsWith(PermissionPolicyPrefix, StringComparison.Ordinal) == true)
            .Select(entry => $"{entry.Location} [Authorize(Policy = \"{entry.Attribute.Policy}\")]")
            .ToArray();

        Assert.True(offenders.Length == 0, "Use [HasPermission(...)] instead of: " + string.Join(", ", offenders));
    }

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

    private static IEnumerable<(string Location, AuthorizeAttribute Attribute)> ControllerAuthorizations()
    {
        foreach (var controller in Controllers())
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
