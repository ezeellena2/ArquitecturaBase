using System.Collections;
using System.Reflection;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// Un lector materializa filas o respuestas de Application (backend.md, "Modelos: Response y Row"): la fila termina en
/// <c>Row</c> y el servicio la traduce a su <c>Response</c>, o el lector arma directo el <c>Response</c>. Los conteos
/// terminan en <c>Counts</c> (ADR 0008). Mira cada tipo de <c>Application.Models</c> que devuelve un método de un
/// <c>I*Reader</c> de <c>Interfaces.Persistence</c>, desenvuelto de <c>Task</c>, <c>PagedResult&lt;T&gt;</c>, las
/// colecciones y los diccionarios.
/// </summary>
public sealed class ReaderOutputNamingTests
{
    private const string PersistenceNamespace = "ArquitecturaBase.Application.Interfaces.Persistence";
    private const string ModelsNamespace = "ArquitecturaBase.Application.Models";

    /// <summary>
    /// Las excepciones del núcleo, con su motivo; las de un módulo vienen de su aporte. Cada una tiene que seguir
    /// apareciendo, o sobra.
    /// </summary>
    private static readonly Dictionary<Type, string> CoreExceptions = new()
    {
        [typeof(UserAccount)] = "la vista de la cuenta de IUserReader, que usan los servicios de acceso tal cual",
    };

    private static readonly MethodInfo[] Methods =
    [
        .. typeof(IUserReader).Assembly.GetTypes()
            .Where(type => type.IsInterface && type.ResidesIn(PersistenceNamespace) && type.Name.EndsWith("Reader", StringComparison.Ordinal))
            .SelectMany(contract => contract.GetMethods().Where(method => !method.IsSpecialName)),
    ];

    [Fact]
    public void Reader_outputs_end_in_row()
    {
        // Si el escaneo no encontrara lectores, la regla pasaría en silencio.
        Assert.NotEmpty(Methods);

        var exceptions = CoreExceptions.Keys
            .Concat(ModuleTestContributions.All.SelectMany(module => module.ReaderOutputExceptions))
            .ToHashSet();
        var outputs = Methods
            .SelectMany(method => Outputs(method.ReturnType).Select(output => (Method: method, Output: output)))
            .Where(pair => pair.Output.ResidesIn(ModelsNamespace) && !pair.Output.IsEnum)
            .ToArray();

        Assert.Empty(outputs
            .Where(pair => !HasReaderOutputName(pair.Output) && !exceptions.Contains(pair.Output))
            .Select(pair => pair.Method.DeclaringType!.Name + "." + pair.Method.Name + ": " + pair.Output.Name));

        // Una excepción que ya ningún lector devuelve se borra.
        Assert.Empty(exceptions.Where(type => !outputs.Any(pair => pair.Output == type)).Select(type => type.Name));
    }

    [Fact]
    public void Detector_unwraps_tasks_pages_collections_and_dictionaries()
    {
        // Caso de control: si el detector no desenvolviera, vería Task o PagedResult y la regla pasaría en silencio.
        Assert.Equal([typeof(ControlRow)], Outputs(typeof(Task<PagedResult<ControlRow>>)));
        Assert.Equal([typeof(ControlRow)], Outputs(typeof(Task<IReadOnlyList<ControlRow>>)));
        Assert.Equal([typeof(ControlRow)], Outputs(typeof(Task<ControlRow?>)));
        Assert.Equal([typeof(Guid), typeof(ControlRow)], Outputs(typeof(Task<IReadOnlyDictionary<Guid, ControlRow>>)));
        Assert.False(HasReaderOutputName(typeof(ControlOutput)));
        Assert.True(HasReaderOutputName(typeof(ControlRow)));
    }

    private static bool HasReaderOutputName(Type type) =>
        type.Name.EndsWith("Row", StringComparison.Ordinal)
        || type.Name.EndsWith("Response", StringComparison.Ordinal)
        || type.Name.EndsWith("Counts", StringComparison.Ordinal);

    /// <summary>Los tipos que devuelve el método, sin Task, Nullable, PagedResult, colecciones ni diccionarios.</summary>
    private static IEnumerable<Type> Outputs(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Outputs(underlying);
        }

        if (type.IsArray)
        {
            return Outputs(type.GetElementType()!);
        }

        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>)
            || type.GetGenericTypeDefinition() == typeof(PagedResult<>)
            || typeof(IEnumerable).IsAssignableFrom(type)))
        {
            return type.GetGenericArguments().SelectMany(Outputs);
        }

        return type == typeof(Task) ? [] : [type];
    }

    private sealed record ControlRow;

    private sealed record ControlOutput;
}
