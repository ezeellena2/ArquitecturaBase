using System.Collections;
using System.Reflection;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// La salida de un servicio termina en <c>Response</c> (Etapa 3, tarea 4): así no se confunde con la proyección de un
/// lector, que termina en <c>Row</c>. Mira el tipo de retorno de cada método de <c>Interfaces.Services</c>, desenvuelto
/// de <c>Task</c>, <c>Result&lt;T&gt;</c>, <c>PagedResult&lt;T&gt;</c> y las colecciones, y solo el de nivel superior:
/// los tipos anidados (<c>LastInvitation</c>, <c>PermissionItem</c>) no entran. Los escalares (<c>Guid</c>,
/// <c>bool</c>) no son de <c>Application.Models</c> y quedan afuera solos.
/// </summary>
public sealed class ServiceOutputNamingTests
{
    private const string ServicesNamespace = "ArquitecturaBase.Application.Interfaces.Services";
    private const string ModelsNamespace = "ArquitecturaBase.Application.Models";
    private const string Suffix = "Response";

    /// <summary>Cada excepción con su motivo. Tiene que seguir apareciendo, o sobra.</summary>
    private static readonly Dictionary<Type, string> Exceptions = new()
    {
        // La devuelven igual el lector (IUserReader.CountByFilterOptionAsync) y el servicio, y sigue el sufijo *Counts
        // que el ADR 0008 fija para los conteos.
        [typeof(UserFilterCounts)] = "Counts, ADR 0008",
    };

    private static readonly MethodInfo[] Methods =
    [
        .. typeof(IUserService).Assembly.GetTypes()
            .Where(type => type.IsInterface && type.ResidesIn(ServicesNamespace))
            .SelectMany(contract => contract.GetMethods().Where(method => !method.IsSpecialName)),
    ];

    [Fact]
    public void Service_outputs_end_in_response()
    {
        // Si el escaneo no encontrara métodos, la regla pasaría en silencio.
        Assert.NotEmpty(Methods);

        var outputs = Methods
            .Select(method => (Method: method, Output: TopLevelOutput(method.ReturnType)))
            .Where(pair => pair.Output.ResidesIn(ModelsNamespace))
            .ToArray();

        Assert.Empty(outputs
            .Where(pair => !pair.Output.Name.EndsWith(Suffix, StringComparison.Ordinal) && !Exceptions.ContainsKey(pair.Output))
            .Select(pair => pair.Method.DeclaringType!.Name + "." + pair.Method.Name + ": " + pair.Output.Name));

        // Una excepción que ya ningún servicio devuelve se borra.
        Assert.Empty(Exceptions.Keys.Where(type => !outputs.Any(pair => pair.Output == type)).Select(type => type.Name));
    }

    [Fact]
    public void Detector_unwraps_results_pages_and_collections()
    {
        // Caso de control: si el detector no desenvolviera, vería Result o PagedResult y la regla pasaría en silencio.
        Assert.Equal(typeof(ControlOutput), TopLevelOutput(typeof(Task<Result<PagedResult<ControlOutput>>>)));
        Assert.Equal(typeof(ControlOutput), TopLevelOutput(typeof(Task<IReadOnlyCollection<ControlOutput>>)));
        Assert.Equal(typeof(ControlOutput), TopLevelOutput(typeof(Task<ControlOutput?>)));
        Assert.Equal(typeof(Guid), TopLevelOutput(typeof(Task<Result<Guid>>)));
        Assert.Equal(typeof(string), TopLevelOutput(typeof(Task<Result<string>>)));
    }

    /// <summary>El tipo que devuelve el método, sin Task, Nullable, Result, PagedResult ni colecciones.</summary>
    private static Type TopLevelOutput(Type type)
    {
        while (true)
        {
            var unwrapped = Unwrap(type);
            if (unwrapped == type)
            {
                return type;
            }

            type = unwrapped;
        }
    }

    private static Type Unwrap(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return underlying;
        }

        if (!type.IsGenericType)
        {
            return type;
        }

        var definition = type.GetGenericTypeDefinition();
        if (definition == typeof(Task<>) || definition == typeof(Result<>) || definition == typeof(PagedResult<>))
        {
            return type.GetGenericArguments()[0];
        }

        return typeof(IEnumerable).IsAssignableFrom(type) && type.GetGenericArguments() is [var element]
            ? element
            : type;
    }

    private sealed record ControlOutput;
}
