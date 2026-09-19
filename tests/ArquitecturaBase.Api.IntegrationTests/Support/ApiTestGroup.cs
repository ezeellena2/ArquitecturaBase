namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>Un solo contenedor y una sola Api para todos los tests HTTP; corren en serie porque comparten el reloj.</summary>
[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
