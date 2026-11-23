namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>Tests que miden todo el proceso, como lo que asigna una lectura: corren solos, después de las colecciones en paralelo, para que
/// lo que asigna otro test al mismo tiempo no cuente como suyo.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AloneTestGroup
{
    public const string Name = "Alone";
}
