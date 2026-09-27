using System.CodeDom.Compiler;
using System.Reflection;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// El lector del IL en el que se apoyan las reglas de TransactionBoundaryTests. Un generador de código fuente emite
/// tipos propios con textos que no son código del proyecto: el de OpenAPI copia como literales la documentación XML de
/// los tipos públicos de Api, Application e Infrastructure, y un <c>///</c> que nombrara un lock contaría como un lock.
/// </summary>
public sealed class CallSitesTests
{
    private static readonly Assembly Api = Assembly.Load("ArquitecturaBase.Api");

    [Fact]
    public void Types_emitted_by_source_generators_are_left_out()
    {
        var generated = Api.GetTypes()
            .Where(type => type.DeclaringType is null && type.IsDefined(typeof(GeneratedCodeAttribute), inherit: false))
            .Select(type => type.FullName!)
            .ToHashSet(StringComparer.Ordinal);

        // Si la Api dejara de usar generadores, este test no probaría nada.
        Assert.NotEmpty(generated);

        var scanned = CallSites.Literals(Api).Select(literal => literal.Owner)
            .Concat(CallSites.Calls(Api).Select(call => call.Owner))
            .Concat(CallSites.TypeUses(Api).Select(use => use.Owner))
            .Where(generated.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(scanned.Length == 0, "Generated types are still scanned: " + string.Join(", ", scanned));
    }
}
