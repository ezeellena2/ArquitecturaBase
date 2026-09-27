using System.CodeDom.Compiler;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ArquitecturaBase.ArchitectureTests.Support;

/// <summary>
/// Las llamadas, los tipos nombrados y los literales de texto de un ensamblado, leídos del IL con Mono.Cecil y agrupados
/// por el tipo de nivel superior que los hace. NetArchTest mira dependencias de tipos, no llamadas, y un escaneo de
/// fuentes se confunde con los comentarios. El IL tampoco está libre de ellos: un generador de código fuente puede
/// embeber la documentación XML como literales (el de OpenAPI copia la de los tipos públicos de Api, Application e
/// Infrastructure), y un <c>///</c> que nombrara un lock contaría como un lock. Por eso se saltean los tipos que emite un
/// generador.
/// </summary>
internal static class CallSites
{
    public sealed record Call(string Owner, string DeclaringType, string Method);

    public sealed record Literal(string Owner, string Value);

    /// <summary>Un tipo que nombra una instrucción de <paramref name="Owner"/>.</summary>
    public sealed record TypeUse(string Owner, string Type);

    public static IReadOnlyList<Call> Calls(Assembly assembly) =>
        Read<Call>(assembly, (owner, instruction) => instruction.Operand is MethodReference called
            ? [new Call(owner, called.DeclaringType.FullName, called.Name)]
            : []);

    public static IReadOnlyList<Literal> Literals(Assembly assembly) =>
        Read<Literal>(assembly, (owner, instruction) => instruction.Operand is string value ? [new Literal(owner, value)] : []);

    /// <summary>
    /// Cada tipo que nombra una instrucción: el que declara el método o el campo que usa, los de su firma, los argumentos
    /// genéricos de la llamada y del tipo, y el operando de un typeof, un cast o un new[]. Así aparece quien construye,
    /// resuelve o llama a una clase aunque la nombre solo como argumento genérico (<c>AddScoped&lt;IX, X&gt;()</c>).
    /// </summary>
    public static IReadOnlyList<TypeUse> TypeUses(Assembly assembly) =>
        Read<TypeUse>(assembly, (owner, instruction) => NamedBy(instruction.Operand)
            .Distinct(StringComparer.Ordinal)
            .Select(type => new TypeUse(owner, type)));

    /// <summary>
    /// Las llamadas a <paramref name="methods"/> que pueden terminar en un tipo que hereda de <paramref name="baseType"/>:
    /// las que Cecil resuelve a un método que declara <paramref name="baseType"/> o un tipo que hereda de él, y las que
    /// resuelve a un método de una interfaz. El nombre del tipo que figura en la llamada no alcanza: un contexto propio
    /// puede llamarse de cualquier forma y esconder o redefinir el método. Si Cecil no puede resolver una llamada
    /// candidata, lanza: la regla no puede decidir y no tiene que pasar callada.
    /// </summary>
    public static IReadOnlyList<Call> CallsThatCanReach(Assembly assembly, Type baseType, IReadOnlyCollection<string> methods)
    {
        ArgumentNullException.ThrowIfNull(baseType);
        ArgumentNullException.ThrowIfNull(methods);

        return Read<Call>(assembly, (owner, instruction) => instruction.Operand is MethodReference called
            && methods.Contains(called.Name, StringComparer.Ordinal)
            && CanReach(called, baseType.FullName!)
                ? [new Call(owner, called.DeclaringType.FullName, called.Name)]
                : []);
    }

    // Se pasa todo a texto antes de soltar el módulo: Cecil lee algunas cosas recién cuando se las pide, y resuelve las
    // referencias con el resolver, que busca las dependencias (EF Core, Identity) al lado del ensamblado.
    private static List<T> Read<T>(Assembly assembly, Func<string, Instruction, IEnumerable<T>> select)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(assembly.Location));
        using var module = ModuleDefinition.ReadModule(assembly.Location, new ReaderParameters { AssemblyResolver = resolver });

        return
        [
            .. module.GetTypes()
                .Where(type => !IsEmittedByGenerator(Outermost(type)))
                .SelectMany(type => type.Methods
                    .Where(method => method.HasBody)
                    .SelectMany(method => method.Body.Instructions
                        .SelectMany(instruction => select(Outermost(type).FullName, instruction)))),
        ];
    }

    private static IEnumerable<string> NamedBy(object? operand) => operand switch
    {
        MethodReference method =>
        [
            .. Expand(method.DeclaringType),
            .. Expand(method.ReturnType),
            .. method.Parameters.SelectMany(parameter => Expand(parameter.ParameterType)),
            .. method is GenericInstanceMethod generic ? generic.GenericArguments.SelectMany(Expand) : [],
        ],
        FieldReference field => [.. Expand(field.DeclaringType), .. Expand(field.FieldType)],
        TypeReference type => Expand(type),
        _ => [],
    };

    // Un tipo y lo que lleva adentro: los argumentos de un genérico y el elemento de un arreglo o de una referencia. Un
    // parámetro genérico (T, !0) no nombra ningún tipo.
    private static IEnumerable<string> Expand(TypeReference type) => type switch
    {
        GenericInstanceType generic => [generic.ElementType.FullName, .. generic.GenericArguments.SelectMany(Expand)],
        TypeSpecification specification => Expand(specification.ElementType),
        GenericParameter => [],
        _ => [type.FullName],
    };

    // El método que de verdad se llama (si la llamada nombra un tipo derivado que no lo redefine, Cecil sube por la
    // herencia hasta encontrarlo) y la cadena de tipos base de quien lo declara. Un método de una interfaz cuenta siempre:
    // un tipo que hereda de baseType la puede implementar con el método heredado, sin que su IL tenga una llamada que ver,
    // y quien la implementa puede vivir en otro ensamblado o ser el propio EF (IStateManager.SaveChanges guarda sin pasar
    // por DbContext). La cadena de una interfaz no tiene tipos base que recorrer.
    private static bool CanReach(MethodReference called, string baseType)
    {
        var method = called.Resolve() ?? throw new InvalidOperationException($"Cecil could not resolve {called.FullName}.");

        if (method.DeclaringType.IsInterface)
        {
            return true;
        }

        for (var type = method.DeclaringType; type is not null; type = BaseOf(type))
        {
            if (type.FullName == baseType)
            {
                return true;
            }
        }

        return false;
    }

    // System.Object no se resuelve: es el final de toda cadena y vive en el runtime, fuera de la carpeta del ensamblado.
    private static TypeDefinition? BaseOf(TypeDefinition type) =>
        type.BaseType is not { } baseType || baseType.FullName == typeof(object).FullName
            ? null
            : baseType.Resolve() ?? throw new InvalidOperationException($"Cecil could not resolve {baseType.FullName}.");

    // El cuerpo de un método async o de una lambda vive en un tipo anidado que genera el compilador: cuenta como de su
    // dueño.
    private static TypeDefinition Outermost(TypeDefinition type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type;
    }

    // Un generador marca así los tipos propios que emite. Lo que agrega a un tipo parcial del proyecto ([LoggerMessage],
    // [GeneratedRegex]) lleva la marca en el miembro, no en el tipo, y se sigue leyendo como de ese tipo.
    private static bool IsEmittedByGenerator(TypeDefinition type) =>
        type.CustomAttributes.Any(attribute =>
            attribute.AttributeType.FullName == typeof(GeneratedCodeAttribute).FullName);
}
