using Xunit.Sdk;
using Xunit.v3;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// Larga primero las colecciones que más tardaron en la última medición (<see cref="ApiTestCollectionFactory.SecondsOf"/>).
/// Con el algoritmo conservador de xUnit cada colección ocupa un hilo hasta terminar: si una de las lentas arrancara al
/// final, alargaría toda la corrida (el 26/11, una de 8 minutos que salió a los 15 la llevó a 23). El desempate por
/// identificador deja el mismo orden en cada corrida.
/// </summary>
public sealed class ApiTestCollectionOrderer : ITestCollectionOrderer
{
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(
        IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : ITestCollection =>
        [.. testCollections
            .OrderByDescending(collection => ApiTestCollectionFactory.SecondsOf(collection))
            .ThenBy(collection => collection.UniqueID, StringComparer.Ordinal)];
}
