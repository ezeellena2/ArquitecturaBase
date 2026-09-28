using System.Reflection;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// El logging de operación en un solo lugar (Etapa 3, tarea 2): <c>OperationLog</c> es el único que registra
/// "Handling {Operation}", "Handled {Operation}" y el fallo con su código; ningún servicio vuelve a declarar su propio
/// <c>[LoggerMessage]</c> con esos textos. Como TransactionBoundaryTests, lee el IL de Application, Infrastructure y Api
/// con Mono.Cecil: el mensaje de un <c>[LoggerMessage]</c> queda como literal en el método que lo genera, y ese método
/// se cuenta como del tipo parcial del proyecto que lo declara (ver <see cref="CallSites"/>).
/// </summary>
public sealed class OperationLoggingTests
{
    private const string OperationLogOwner = "ArquitecturaBase.Application.Common.Logging.OperationLog";

    private static readonly Assembly[] Scanned =
    [
        Assembly.Load("ArquitecturaBase.Application"),
        Assembly.Load("ArquitecturaBase.Infrastructure"),
        Assembly.Load("ArquitecturaBase.Api"),
    ];

    private static readonly CallSites.Literal[] Literals = [.. Scanned.SelectMany(assembly => CallSites.Literals(assembly))];

    // Los dos prefijos que solo OperationLog puede emitir. El texto exacto incluye el espacio final, así "Handled" no se
    // confundiría con una palabra que solo empieza igual.
    private static readonly string[] ReservedPrefixes = ["Handling ", "Handled "];

    [Fact]
    public void Only_operation_log_declares_handling_or_handled_literals()
    {
        var owners = HandlingOrHandledOwners(Literals);

        // Caso de control: el propio detector, corrido sobre este ensamblado de tests, tiene que encontrar a
        // BadOperationLogger (al pie de este archivo), o la regla estaría pasando en silencio.
        var controlOwners = HandlingOrHandledOwners(CallSites.Literals(typeof(OperationLoggingTests).Assembly));
        Assert.Contains(typeof(BadOperationLogger).FullName, controlOwners);

        // Assert.Equal y no Empty: también prueba que el detector ve a OperationLog, el único dueño permitido.
        Assert.Equal([OperationLogOwner], owners);
    }

    private static string[] HandlingOrHandledOwners(IEnumerable<CallSites.Literal> literals) =>
    [
        .. literals
            .Where(literal => ReservedPrefixes.Any(prefix => literal.Value.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(literal => literal.Owner)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];
}

// El caso de control de Only_operation_log_declares_handling_or_handled_literals. Va fuera de OperationLoggingTests, con
// su propio dueño, y no se ejecuta nunca: solo importa su IL, corrido sobre el ensamblado de tests. No usa
// [LoggerMessage] a propósito: al detector le da igual de dónde salga el literal, y así se evita depender del generador.
file static class BadOperationLogger
{
    public static string Handling() => "Handling ControlCase";
}
