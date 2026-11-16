using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// La edición de una cuenta, de un rol, del perfil y de la configuración compara una revisión bajo el lock de la fila y
/// responde 409 si cambió (N2, ADR 0018): el cuerpo de cada una lleva la revisión que leyó la pantalla. Sin ella, el último
/// en guardar pisa al anterior sin aviso. Cuenta la escritura del recurso mismo (PUT o PATCH sin subruta); las que confirma
/// un código (el correo del perfil) o que llevan el estado en la ruta (activar, desactivar) no editan un borrador.
/// </summary>
public sealed class RevisionContractTests
{
    private static readonly Assembly ApiAssembly = Assembly.Load("ArquitecturaBase.Api");

    private static readonly string[] EditedResources =
    [
        "ArquitecturaBase.Api.Controllers.UsersController",
        "ArquitecturaBase.Api.Controllers.RolesController",
        "ArquitecturaBase.Api.Controllers.MeController",
        "ArquitecturaBase.Api.Controllers.SettingsController",
    ];

    [Fact]
    public void Writes_of_users_roles_profile_and_settings_carry_the_expected_revision()
    {
        var writes = ResourceWrites(EditedResources.Select(name => ApiAssembly.GetType(name, throwOnError: true)!));

        // Primero lo que falta, con el nombre de cada acción: una escritura nueva sin revisión se ve acá, no en el conteo.
        var offenders = OffendersAmong(writes);
        Assert.True(
            offenders.Length == 0,
            "These PUT or PATCH actions edit the resource without a long ExpectedRevision in their body (N2, ADR 0018): "
            + string.Join(", ", offenders));

        // Si la búsqueda dejara de encontrarlas, la regla pasaría en silencio: hoy son la edición de una cuenta, de un rol,
        // del perfil y de una sección de la configuración.
        Assert.True(writes.Length >= 4, $"Expected at least 4 resource writes, found {writes.Length}: the search broke.");
    }

    [Fact]
    public void The_rule_reports_a_resource_write_without_the_expected_revision()
    {
        var writes = ResourceWrites([typeof(ControllerWithoutRevision)]);

        Assert.Equal(["ControllerWithoutRevision.Update"], OffendersAmong(writes));
    }

    private static MethodInfo[] ResourceWrites(IEnumerable<Type> controllers) =>
        [.. controllers
            .SelectMany(controller => controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(EditsTheResourceItself)];

    private static string[] OffendersAmong(IEnumerable<MethodInfo> writes) =>
        [.. writes
            .Where(action => BodyOf(action)?.GetProperty("ExpectedRevision") is not { PropertyType: var type } || type != typeof(long))
            .Select(action => $"{action.DeclaringType!.Name}.{action.Name}")];

    // PUT o PATCH sobre la ruta del recurso: sin plantilla o sólo con su id.
    private static bool EditsTheResourceItself(MethodInfo action) =>
        action.GetCustomAttributes<HttpMethodAttribute>()
            .Any(attribute => attribute is HttpPutAttribute or HttpPatchAttribute
                && (string.IsNullOrEmpty(attribute.Template) || attribute.Template == "{id:guid}"));

    private static Type? BodyOf(MethodInfo action) =>
        action.GetParameters()
            .SingleOrDefault(parameter => parameter.GetCustomAttribute<FromBodyAttribute>() is not null)?.ParameterType;

    /// <summary>Una edición sin revisión y una con ella, para ver que la regla separa las dos.</summary>
    private sealed class ControllerWithoutRevision
    {
        public object? Received { get; private set; }

        [HttpPut("{id:guid}")]
        public void Update([FromRoute] Guid id, [FromBody] BodyWithoutRevision body) => Received = (id, body);

        [HttpPatch]
        public void Patch([FromBody] BodyWithRevision body) => Received = body;
    }

    private sealed record BodyWithoutRevision(string Name);

    private sealed record BodyWithRevision(string Name, long ExpectedRevision);
}
