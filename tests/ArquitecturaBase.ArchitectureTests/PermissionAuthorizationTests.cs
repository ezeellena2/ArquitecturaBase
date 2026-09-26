using System.Reflection;
using ArquitecturaBase.Api.Authorization;
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

    private static IEnumerable<(string Location, AuthorizeAttribute Attribute)> ControllerAuthorizations()
    {
        var controllers = ApiAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(type))
            .ToArray();

        Assert.NotEmpty(controllers);

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
