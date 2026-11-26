using Xunit.Sdk;
using Xunit.v3;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// Larga primero las colecciones con más casos. Con el algoritmo conservador de xUnit cada colección ocupa un hilo hasta
/// terminar: si la más pesada arrancara última, alargaría toda la corrida. El desempate por identificador deja el mismo
/// orden en cada corrida.
/// </summary>
public sealed class ApiTestCollectionOrderer : ITestCollectionOrderer
{
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(
        IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : ITestCollection =>
        [.. testCollections
            .OrderByDescending(collection => ApiTestCollectionFactory.CasesOf(collection))
            .ThenBy(collection => collection.UniqueID, StringComparer.Ordinal)];
}
