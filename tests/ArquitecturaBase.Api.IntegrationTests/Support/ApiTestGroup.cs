namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>Los tests HTTP que comparten una Api con su Postgres, su Redis y su reloj: dentro de una colección corren en
/// serie. <see cref="ApiTestCollectionFactory"/> reparte las clases en varias colecciones, cada una con su ApiFactory.</summary>
[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
