using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;
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

    /// <summary>Una llamada con la cantidad de parámetros del método que nombra: distingue las sobrecargas de un constructor.</summary>
    public sealed record SizedCall(string Owner, string DeclaringType, string Method, int Parameters);

    public sealed record Literal(string Owner, string Value);

    /// <summary>Un tipo que nombra una instrucción de <paramref name="Owner"/>.</summary>
    public sealed record TypeUse(string Owner, string Type);

    /// <summary>Cuántas llamadas hay en el recorrido de <paramref name="Method"/>, un método público de <paramref name="Owner"/>.</summary>
    public sealed record PathCalls(string Owner, string Method, int Count);

    public static IReadOnlyList<Call> Calls(Assembly assembly) =>
        Read<Call>(assembly, (owner, instruction) => instruction.Operand is MethodReference called
            ? [new Call(owner, called.DeclaringType.FullName, called.Name)]
            : []);

    public static IReadOnlyList<SizedCall> SizedCalls(Assembly assembly) =>
        Read<SizedCall>(assembly, (owner, instruction) => instruction.Operand is MethodReference called
            ? [new SizedCall(owner, called.DeclaringType.FullName, called.Name, called.Parameters.Count)]
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

    /// <summary>
    /// Cuántas llamadas a <paramref name="method"/> de <paramref name="declaringType"/> hay en el recorrido de cada método
    /// público de un tipo de nivel superior que la hace: su cuerpo, los métodos del mismo tipo que llama (privados,
    /// funciones locales), las lambdas que arma y la máquina de estado de un método async. Cada llamada suma, así un
    /// privado que la hace y se llama dos veces cuenta dos. Lo que se llama de otro tipo no se sigue, y una llamada dentro
    /// de un bucle o de una recursión cuenta una vez. Solo aparecen los métodos que llegan a alguna.
    /// </summary>
    public static IReadOnlyList<PathCalls> CallsOnPublicPaths(Assembly assembly, string declaringType, string method)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(method);

        bool IsTarget(MethodReference called) => called.Name == method && called.DeclaringType.FullName == declaringType;

        return Open<PathCalls>(assembly, module =>
        [
            .. module.Types
                .Where(type => !IsEmittedByGenerator(type) && SelfAndNested(type).Any(own => own.Methods
                    .Any(candidate => candidate.HasBody && candidate.Body.Instructions
                        .Any(instruction => instruction.Operand is MethodReference called && IsTarget(called)))))
                .SelectMany(type => type.Methods
                    .Where(candidate => candidate.IsPublic && candidate.HasBody)
                    .Select(candidate => new PathCalls(type.FullName, candidate.Name, CountOnPath(candidate, type, IsTarget, []))))
                .Where(path => path.Count > 0),
        ]);
    }

    private static List<T> Read<T>(Assembly assembly, Func<string, Instruction, IEnumerable<T>> select) =>
        Open<T>(assembly, module =>
        [
            .. module.GetTypes()
                .Where(type => !IsEmittedByGenerator(Outermost(type)))
                .SelectMany(type => type.Methods
                    .Where(method => method.HasBody)
                    .SelectMany(method => method.Body.Instructions
                        .SelectMany(instruction => select(Outermost(type).FullName, instruction)))),
        ]);

    // Se pasa todo a texto antes de soltar el módulo: Cecil lee algunas cosas recién cuando se las pide, y resuelve las
    // referencias con el resolver, que busca las dependencias (EF Core, Identity) al lado del ensamblado.
    private static List<T> Open<T>(Assembly assembly, Func<ModuleDefinition, List<T>> read)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(assembly.Location));
        using var module = ModuleDefinition.ReadModule(assembly.Location, new ReaderParameters { AssemblyResolver = resolver });

        return read(module);
    }

    // Las llamadas a la buscada en el cuerpo de un método, más las de los métodos de owner que nombra (un call, o el ldftn
    // de una lambda o de un grupo de métodos) y las de su máquina de estado. Un método que ya está en el camino suma cero:
    // así una recursión no da vueltas para siempre.
    private static int CountOnPath(
        MethodDefinition method,
        TypeDefinition owner,
        Func<MethodReference, bool> isTarget,
        HashSet<MethodDefinition> path)
    {
        if (!path.Add(method))
        {
            return 0;
        }

        var count = method.Body.Instructions
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .Sum(called => isTarget(called) ? 1
                : OwnMethod(called, owner) is { HasBody: true } own ? CountOnPath(own, owner, isTarget, path)
                : 0);

        if (StateMachine(method) is { } moveNext)
        {
            count += CountOnPath(moveNext, owner, isTarget, path);
        }

        path.Remove(method);

        return count;
    }

    // Un método de owner o de uno de sus tipos anidados, que es donde el compilador deja las lambdas, las funciones locales
    // y las máquinas de estado. Vive en el mismo módulo, así que Cecil lo resuelve sin buscar otros ensamblados.
    private static MethodDefinition? OwnMethod(MethodReference called, TypeDefinition owner)
    {
        for (var type = called.DeclaringType.GetElementType(); type is not null; type = type.DeclaringType)
        {
            if (type.FullName == owner.FullName)
            {
                return called.Resolve() ?? throw new InvalidOperationException($"Cecil could not resolve {called.FullName}.");
            }
        }

        return null;
    }

    // El cuerpo de un método async o iterador vive en el MoveNext del tipo que nombra su atributo: el método solo arma la
    // máquina y la arranca.
    private static MethodDefinition? StateMachine(MethodDefinition method) =>
        method.CustomAttributes
            .Where(attribute => attribute.AttributeType.Namespace == typeof(StateMachineAttribute).Namespace
                && attribute.AttributeType.Name.EndsWith(nameof(StateMachineAttribute), StringComparison.Ordinal))
            .Select(attribute => ((TypeReference)attribute.ConstructorArguments[0].Value).Resolve()
                ?? throw new InvalidOperationException($"Cecil could not resolve the state machine of {method.FullName}."))
            .Select(machine => machine.Methods.Single(candidate => candidate.Name == nameof(IAsyncStateMachine.MoveNext)))
            .SingleOrDefault();

    private static IEnumerable<TypeDefinition> SelfAndNested(TypeDefinition type) =>
        [type, .. type.NestedTypes.SelectMany(SelfAndNested)];

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
