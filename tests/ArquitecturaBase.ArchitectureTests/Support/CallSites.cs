using System.CodeDom.Compiler;
using System.Reflection;
using Mono.Cecil;

namespace ArquitecturaBase.ArchitectureTests.Support;

/// <summary>
/// Las llamadas y los literales de texto de un ensamblado, leídos del IL con Mono.Cecil y agrupados por el tipo de nivel
/// superior que los hace. NetArchTest mira dependencias de tipos, no llamadas, y un escaneo de fuentes se confunde con
/// los comentarios. El IL tampoco está libre de ellos: un generador de código fuente puede embeber la documentación XML
/// como literales (el de OpenAPI copia la de los tipos públicos de Api, Application e Infrastructure), y un <c>///</c>
/// que nombrara un lock contaría como un lock. Por eso se saltean los tipos que emite un generador.
/// </summary>
internal static class CallSites
{
    public sealed record Call(string Owner, string DeclaringType, string Method);

    public sealed record Literal(string Owner, string Value);

    public static IReadOnlyList<Call> Calls(Assembly assembly) =>
        Read(assembly, (owner, instruction) => instruction.Operand is MethodReference called
            ? new Call(owner, called.DeclaringType.FullName, called.Name)
            : null);

    public static IReadOnlyList<Literal> Literals(Assembly assembly) =>
        Read(assembly, (owner, instruction) => instruction.Operand is string value ? new Literal(owner, value) : null);

    // Se pasa todo a texto antes de soltar el módulo: Cecil lee algunas cosas recién cuando se las pide.
    private static List<T> Read<T>(Assembly assembly, Func<string, Mono.Cecil.Cil.Instruction, T?> select)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(assembly);

        using var module = ModuleDefinition.ReadModule(assembly.Location);

        return
        [
            .. module.GetTypes()
                .Where(type => !IsEmittedByGenerator(Outermost(type)))
                .SelectMany(type => type.Methods
                    .Where(method => method.HasBody)
                    .SelectMany(method => method.Body.Instructions
                        .Select(instruction => select(Outermost(type).FullName, instruction))))
                .OfType<T>(),
        ];
    }

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
