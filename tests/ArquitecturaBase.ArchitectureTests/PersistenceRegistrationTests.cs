using System.Reflection;
using System.Runtime.CompilerServices;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// Los repositorios, los lectores y los seeders se usan por su contrato: la clase concreta la nombra solo
/// PersistenceRegistration, que la registra (Etapa 7, tarea 6). Lee el IL de Application, Infrastructure y Api con el
/// mismo detector que <see cref="TransactionBoundaryTests.Only_the_registration_names_the_concrete_unit_of_work"/>.
/// Los tests de integración construyen repositorios concretos a propósito (para sostener una fila o forzar un orden de
/// locks) y quedan fuera: sus ensamblados no se miran.
/// </summary>
public sealed class PersistenceRegistrationTests
{
    private const string PersistenceRegistration = "ArquitecturaBase.Infrastructure.Persistence.PersistenceRegistration";

    // Por namespace y no por sufijo: WhatsAppWebhookReader se llama "Reader" y es un parser, no un lector.
    private static readonly string[] PersistenceNamespaces =
    [
        "ArquitecturaBase.Infrastructure.Persistence.Repositories",
        "ArquitecturaBase.Infrastructure.Persistence.Readers",
        "ArquitecturaBase.Infrastructure.Persistence.Seed",
    ];

    // Además de la registración y de la propia clase, solo quien corre el seed nombra un seeder por su clase:
    // SeedExtensions resuelve DatabaseSeeder en un scope propio, y DatabaseSeeder recibe los tres seeders y los corre en
    // su límite (tarea 8 de la Etapa 7). Los seeders no tienen contrato: nadie más los usa.
    private static readonly string[] AllowedOwners =
    [
        PersistenceRegistration,
        "ArquitecturaBase.Infrastructure.Persistence.Seed.SeedExtensions",
        "ArquitecturaBase.Infrastructure.Persistence.Seed.DatabaseSeeder",
    ];

    private static readonly Assembly Infrastructure = Assembly.Load("ArquitecturaBase.Infrastructure");

    private static readonly Assembly[] Scanned =
    [
        Assembly.Load("ArquitecturaBase.Application"),
        Infrastructure,
        Assembly.Load("ArquitecturaBase.Api"),
    ];

    [Fact]
    public void Only_the_registration_names_a_concrete_repository_reader_or_seeder()
    {
        var concrete = Infrastructure.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsNested: false }
                && !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
                && PersistenceNamespaces.Contains(type.Namespace, StringComparer.Ordinal))
            .ToArray();

        // Si un namespace cambiara de nombre, la regla dejaría de mirarlo y pasaría en silencio.
        Assert.All(PersistenceNamespaces, name => Assert.Contains(concrete, type => type.Namespace == name));

        var names = concrete.Select(type => type.FullName!).ToHashSet(StringComparer.Ordinal);

        var uses = Scanned
            .SelectMany(assembly => CallSites.TypeUses(assembly))
            .Where(use => names.Contains(use.Type) && use.Owner != use.Type)
            .ToArray();

        // Caso de control: la registración nombra cada clase como argumento genérico de AddScoped. Si el detector dejara
        // de verla, falla acá en lugar de pasar en silencio.
        var registered = uses.Where(use => use.Owner == PersistenceRegistration).Select(use => use.Type).ToHashSet();
        Assert.All(PersistenceNamespaces, name => Assert.Contains(registered, type => type.StartsWith(name + ".", StringComparison.Ordinal)));

        var violations = uses
            .Where(use => !AllowedOwners.Contains(use.Owner, StringComparer.Ordinal))
            .Select(use => $"{use.Owner} -> {use.Type}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // El mensaje nombra quién y qué: Assert.Empty recortaría los nombres completos.
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }
}
