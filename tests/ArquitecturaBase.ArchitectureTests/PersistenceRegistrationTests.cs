using System.Reflection;
using System.Runtime.CompilerServices;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// Los repositorios, los lectores y los seeders se usan por su contrato: la clase concreta la nombra solo
/// PersistenceRegistration, que la registra (Etapa 7, tarea 6), o, si es de un módulo opcional, el registro de
/// Infrastructure de su módulo (<c>ArquitecturaBase.Infrastructure.Modules.&lt;M&gt;.&lt;M&gt;InfrastructureRegistration</c>).
/// Lee el IL de Application, Infrastructure y Api con el mismo detector que
/// <see cref="TransactionBoundaryTests.Only_the_registration_names_the_concrete_unit_of_work"/>. Los tests de integración
/// construyen repositorios concretos a propósito (para sostener una fila o forzar un orden de locks) y quedan fuera: sus
/// ensamblados no se miran.
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

    private const string SeedNamespace = "ArquitecturaBase.Infrastructure.Persistence.Seed";

    // Además de la registración y de la propia clase, solo quien corre el seed nombra un seeder por su clase:
    // SeedExtensions resuelve DatabaseSeeder en un scope propio, y DatabaseSeeder recibe los seeders y los corre en su
    // límite (tarea 8 de la Etapa 7). Los seeders no tienen contrato: nadie más los usa. La excepción vale solo para los
    // tipos del namespace Seed: ninguno de los dos nombra un repositorio ni un lector concretos.
    private static readonly string[] SeedOwners = [SeedNamespace + ".SeedExtensions", SeedNamespace + ".DatabaseSeeder"];

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
                && IsPersistenceNamespace(type.Namespace))
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

        var violations = Violations(uses);

        // El mensaje nombra quién y qué: Assert.Empty recortaría los nombres completos.
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void The_seed_owners_may_name_only_seed_types()
    {
        // Casos de control con usos armados a mano: el detector acepta a quien corre el seed sobre un tipo de Seed y
        // rechaza que nombre un repositorio o un lector, o que otro tipo nombre un seeder.
        Assert.Empty(Violations([new CallSites.TypeUse(SeedOwners[1], SeedNamespace + ".RoleSeeder")]));
        Assert.Empty(Violations([new CallSites.TypeUse(SeedOwners[0], SeedOwners[1])]));
        Assert.NotEmpty(Violations([new CallSites.TypeUse(SeedOwners[1], PersistenceNamespaces[0] + ".UserRepository")]));
        Assert.NotEmpty(Violations([new CallSites.TypeUse(SeedOwners[0], PersistenceNamespaces[1] + ".UserReader")]));
        Assert.NotEmpty(Violations([new CallSites.TypeUse("ArquitecturaBase.Api.SomeController", SeedNamespace + ".RoleSeeder")]));
    }

    [Fact]
    public void A_module_registration_may_name_only_the_types_of_its_module()
    {
        // Casos de control con módulos inventados: el registro de Infrastructure de un módulo nombra un repositorio suyo,
        // pero no uno del núcleo ni uno de otro módulo, y otro tipo del módulo no nombra ninguno.
        const string ControlRegistration = "ArquitecturaBase.Infrastructure.Modules.Control.ControlInfrastructureRegistration";
        const string ControlRepository = "ArquitecturaBase.Infrastructure.Modules.Control.Persistence.Repositories.ControlRepository";

        Assert.Empty(Violations([new CallSites.TypeUse(ControlRegistration, ControlRepository)]));
        Assert.NotEmpty(Violations([new CallSites.TypeUse(ControlRegistration, PersistenceNamespaces[0] + ".UserRepository")]));
        Assert.NotEmpty(Violations([new CallSites.TypeUse(
            ControlRegistration, "ArquitecturaBase.Infrastructure.Modules.Sms.Persistence.Repositories.SmsRepository")]));
        Assert.NotEmpty(Violations([new CallSites.TypeUse(
            "ArquitecturaBase.Infrastructure.Modules.Control.ControlSender", ControlRepository)]));

        // Y los repositorios, lectores y seeders de un módulo son de persistencia, como los del núcleo.
        Assert.True(IsPersistenceNamespace("ArquitecturaBase.Infrastructure.Modules.Control.Persistence.Repositories"));
        Assert.False(IsPersistenceNamespace("ArquitecturaBase.Infrastructure.Modules.Control.Persistence"));
    }

    /// <summary>Si es uno de los namespaces de persistencia, del núcleo o de un módulo.</summary>
    private static bool IsPersistenceNamespace(string? @namespace) =>
        @namespace is not null && PersistenceNamespaces.Contains(ModuleNamespaces.Canonical(@namespace), StringComparer.Ordinal);

    /// <summary>Si quien nombra la clase es el registro de Infrastructure de un módulo, y la clase es de ese módulo.</summary>
    private static bool IsTheRegistrationOfItsModule(CallSites.TypeUse use) =>
        ModuleNamespaces.ModuleOf(use.Owner) is { } module
        && use.Owner == $"ArquitecturaBase.Infrastructure.Modules.{module}.{module}InfrastructureRegistration"
        && ModuleNamespaces.ModuleOf(use.Type) == module;

    private static string[] Violations(IEnumerable<CallSites.TypeUse> uses) =>
    [
        .. uses
            .Where(use => use.Owner != PersistenceRegistration
                && !IsTheRegistrationOfItsModule(use)
                && !(SeedOwners.Contains(use.Owner, StringComparer.Ordinal)
                    && use.Type.StartsWith(SeedNamespace + ".", StringComparison.Ordinal)))
            .Select(use => $"{use.Owner} -> {use.Type}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];
}
