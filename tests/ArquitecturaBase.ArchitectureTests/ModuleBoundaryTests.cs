using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ArquitecturaBase.ArchitectureTests.Support;
using ArquitecturaBase.Domain.Modules.Control;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.ArchitectureTests
{
    /// <summary>
    /// La frontera de un módulo opcional (ADR 0007, Etapa 6): vive en las carpetas <c>Modules/&lt;M&gt;</c> de los mismos
    /// proyectos, el núcleo no lo nombra, un módulo no nombra a otro, el contexto no expone sus entidades y Program.cs lo
    /// compone con un registro por capa. Es lo que permite quitarlo borrando sus carpetas y su bloque de Program.cs. Los
    /// casos de control usan módulos inventados (Control y Sms): estos tests son del núcleo y corren sin ningún módulo.
    /// </summary>
    public sealed partial class ModuleBoundaryTests
    {
        private const string ProgramFile = "src/ArquitecturaBase.Api/Program.cs";

        private const string MigrationsFolder = "src/ArquitecturaBase.Infrastructure/Persistence/Migrations/";

        private static readonly string[] Projects =
        [
            "ArquitecturaBase.Domain",
            "ArquitecturaBase.Application",
            "ArquitecturaBase.Infrastructure",
            "ArquitecturaBase.Api",
        ];

        // Las capas que registran lo suyo en DI: Domain no tiene registro.
        private static readonly string[] RegisteredLayers = ["Application", "Infrastructure", "Api"];

        /// <summary>
        /// Los archivos del núcleo que todavía nombran un módulo, cada uno con la tanda de la Etapa 6 que lo arregla. Se
        /// compara en los dos sentidos: un archivo que ya no nombra el módulo también falla, y obliga a achicar la lista.
        /// </summary>
        private static readonly Dictionary<string, int> KnownViolations = new(StringComparer.Ordinal)
        {
            ["src/ArquitecturaBase.Application/Interfaces/Services/ILoginCodeService.cs"] = 6,
            ["src/ArquitecturaBase.Application/Services/Auth/DestinationCodeIssuer.cs"] = 6,
            ["src/ArquitecturaBase.Application/Services/Auth/LoginCodeIssuer.cs"] = 6,
            ["src/ArquitecturaBase.Application/Services/Auth/LoginCodeService.cs"] = 6,
            ["src/ArquitecturaBase.Application/Services/Auth/SignInCodeIssuer.cs"] = 6,
            ["src/ArquitecturaBase.Application/Services/Users/PhoneNumberLinker.cs"] = 7,
        };

        private static readonly Assembly[] Assemblies = [.. Projects.Select(Assembly.Load)];

        [Fact]
        public void The_core_does_not_name_a_module_and_a_module_does_not_name_another()
        {
            var files = SourceFiles();

            // Que el detector mire código real: los cuatro proyectos, con Program.cs, la única excepción.
            Assert.True(files.Count > 100, $"Only {files.Count} source files were scanned.");
            Assert.Contains(ProgramFile, files.Keys);

            var violations = files
                .Where(file => !KnownViolations.ContainsKey(file.Key))
                .SelectMany(file => Violations(file.Key, file.Value))
                .ToArray();
            var fixedFiles = KnownViolations.Keys
                .Where(path => !files.TryGetValue(path, out var text) || !Violations(path, text).Any())
                .Order(StringComparer.Ordinal)
                .ToArray();

            // El mensaje nombra cada archivo y cada módulo: Assert.Empty o Assert.Equal recortarían las rutas.
            Assert.True(
                violations.Length == 0,
                "The core names a module:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
            Assert.True(
                fixedFiles.Length == 0,
                "These known violations no longer name a module; remove them from KnownViolations:" + Environment.NewLine
                    + string.Join(Environment.NewLine, fixedFiles));
        }

        [Fact]
        public void The_source_rule_reports_the_core_naming_a_module_and_a_module_naming_another()
        {
            const string CoreFile = "src/ArquitecturaBase.Application/Services/Users/X.cs";
            const string ModuleFile = "src/ArquitecturaBase.Application/Modules/Control/Services/X.cs";

            // El núcleo no nombra un módulo: ni con un using, ni en un cref, ni con global::, ni en el .csproj.
            Assert.NotEmpty(Violations(CoreFile, "using ArquitecturaBase.Application.Modules.Control.Services;"));
            Assert.NotEmpty(Violations(CoreFile, "/// <see cref=\"ArquitecturaBase.Domain.Modules.Control.ControlMessage\"/>"));
            Assert.NotEmpty(Violations(CoreFile, "var name = nameof(global::ArquitecturaBase.Api.Modules.Control.X);"));
            Assert.NotEmpty(Violations(
                "src/ArquitecturaBase.Application/ArquitecturaBase.Application.csproj",
                "<Using Include=\"ArquitecturaBase.Application.Modules.Control\" />"));

            // Un módulo nombra lo suyo en cualquier capa, pero no a otro módulo.
            Assert.Empty(Violations(ModuleFile, "using ArquitecturaBase.Application.Modules.Control.Services;"));
            Assert.Empty(Violations(ModuleFile, "using ArquitecturaBase.Domain.Modules.Control;"));
            Assert.NotEmpty(Violations(ModuleFile, "using ArquitecturaBase.Api.Modules.Sms;"));

            // Program.cs compone los módulos: es la única excepción.
            Assert.Empty(Violations(ProgramFile, "using ArquitecturaBase.Api.Modules.Sms;"));

            // El punto después de Modules importa: ModulesLegacy no es un módulo.
            Assert.Empty(Violations(CoreFile, "using ArquitecturaBase.Application.ModulesLegacy;"));

            // Lo que genera EF en las migraciones (el snapshot y los .Designer.cs) nombra las entidades con cadenas, que
            // compilan sin el módulo; una migración escrita a mano, en cambio, cuenta.
            const string GeneratedEntity =
                "modelBuilder.Entity(\"ArquitecturaBase.Domain.Modules.Control.ControlMessage\", b => { });";
            Assert.Empty(Violations(MigrationsFolder + "ApplicationDbContextModelSnapshot.cs", GeneratedEntity));
            Assert.Empty(Violations(MigrationsFolder + "20990101000000_Control.Designer.cs", GeneratedEntity));
            Assert.NotEmpty(Violations(
                MigrationsFolder + "20990101000000_Control.cs", "using ArquitecturaBase.Domain.Modules.Control;"));
        }

        [Fact]
        public void The_context_does_not_expose_the_entities_of_a_module()
        {
            // Que el detector mire algo: el contexto expone conjuntos del núcleo.
            Assert.Contains(typeof(ApplicationDbContext).GetProperties(), property => IsDbSet(property.PropertyType));

            // Un módulo usa dbContext.Set<T>(): una propiedad del contexto no compilaría sin él.
            Assert.Empty(ModuleSets(typeof(ApplicationDbContext)));
        }

        [Fact]
        public void The_context_rule_reports_a_set_of_a_module_entity()
        {
            Assert.Equal([nameof(ControlContext.Entities)], ModuleSets(typeof(ControlContext)));
        }

        [Fact]
        public void Program_composes_every_module_with_a_registration_per_layer()
        {
            var program = File.ReadAllText(Path.Combine(SolutionRoot.FullPath, ProgramFile));
            var typeNames = Assemblies.SelectMany(assembly => assembly.GetTypes()).Select(type => type.FullName!).ToArray();
            var registrations = Registrations(Assemblies);

            var modules = ModuleFolders()
                .Concat(typeNames.Select(ModuleNamespaces.ModuleOf).OfType<string>())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);

            var problems = modules
                .SelectMany(module => ModuleProblems(module, typeNames, registrations, program))
                .ToArray();

            Assert.True(
                problems.Length == 0,
                "A module is not composed:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }

        [Fact]
        public void The_registration_rule_reports_a_layer_without_its_registration_and_program_without_the_module()
        {
            string[] typeNames =
            [
                "ArquitecturaBase.Domain.Modules.Control.ControlMessage",
                "ArquitecturaBase.Application.Modules.Control.Services.ControlService",
                "ArquitecturaBase.Application.Modules.Control.ControlApplicationRegistration",
                "ArquitecturaBase.Infrastructure.Modules.Control.ControlInfrastructureRegistration",
                "ArquitecturaBase.Api.Modules.Control.Controllers.ControlController",

                // Un módulo sin tipos en Api ni en Infrastructure no necesita esos registros.
                "ArquitecturaBase.Application.Modules.Sms.SmsApplicationRegistration",
            ];
            string[] registrations =
            [
                "ArquitecturaBase.Application.Modules.Control.ControlApplicationRegistration.AddControlApplication",
                "ArquitecturaBase.Infrastructure.Modules.Control.ControlInfrastructureRegistration.AddControlInfrastructure",
                "ArquitecturaBase.Application.Modules.Sms.SmsApplicationRegistration.AddSmsApplication",
            ];
            const string ProgramText = """
                using ArquitecturaBase.Application.Modules.Control;
                using ArquitecturaBase.Infrastructure.Modules.Control;

                builder.Services
                    .AddControlApplication()
                    .AddControlInfrastructure(builder.Configuration);
                """;

            // Control tiene tipos en Api y le falta su registro; Program.cs no llama a lo que falta.
            var control = ModuleProblems("Control", typeNames, registrations, ProgramText).ToArray();
            Assert.Equal(2, control.Length);
            Assert.All(control, problem => Assert.Contains("ControlApiRegistration.AddControlApi", problem, StringComparison.Ordinal));

            // Sms tiene su único registro, pero Program.cs no lo nombra ni lo llama; no se le piden los de Infrastructure
            // ni Api, porque no tiene tipos en esas capas.
            Assert.Equal(
                [
                    "Sms: Program.cs does not name the module",
                    "Sms: Program.cs does not call ArquitecturaBase.Application.Modules.Sms.SmsApplicationRegistration.AddSmsApplication",
                ],
                ModuleProblems("Sms", typeNames, registrations, ProgramText));

            // Con todo en su lugar, nada.
            string[] complete = [.. registrations, "ArquitecturaBase.Api.Modules.Control.ControlApiRegistration.AddControlApi"];
            Assert.Empty(ModuleProblems("Control", typeNames, complete, ProgramText + ".AddControlApi();"));
        }

        [Fact]
        public void A_registration_is_a_public_static_class_with_its_add_extension()
        {
            // Casos de control al pie del archivo: el de Application tiene la forma; al de Infrastructure le falta el
            // nombre del método y al de Api, ser público.
            Assert.Equal(
                ["ArquitecturaBase.Application.Modules.Control.ControlApplicationRegistration.AddControlApplication"],
                Registrations([typeof(ModuleBoundaryTests).Assembly]));
        }

        /// <summary>
        /// Lo que <paramref name="relativePath"/> (con barras normales, desde la raíz) nombra de un módulo que no es el
        /// suyo: cualquier módulo si es del núcleo, otro módulo si vive en <c>Modules/&lt;M&gt;</c>. Program.cs, que los
        /// compone, queda afuera, y también lo que EF genera en las migraciones (ver
        /// <see cref="IsGeneratedByEntityFramework"/>). Lee el texto y no el IL: una constante, un <c>cref</c> o un
        /// <c>nameof</c> no dejan rastro en el IL y rompen el build al borrar el módulo.
        /// </summary>
        private static IEnumerable<string> Violations(string relativePath, string text)
        {
            if (relativePath == ProgramFile || IsGeneratedByEntityFramework(relativePath))
            {
                return [];
            }

            var own = ModuleFolder().Match(relativePath) is { Success: true } folder ? folder.Groups["module"].Value : null;

            return ModuleNames(text)
                .Where(module => module != own)
                .Select(module => $"{relativePath} names the module {module}");
        }

        /// <summary>
        /// El snapshot y los <c>.Designer.cs</c> de las migraciones: EF nombra ahí cada entidad con una cadena
        /// (<c>modelBuilder.Entity("…")</c>), que compila sin el módulo. Una migración escrita a mano no entra: un
        /// <c>using</c> o un <c>typeof</c> de un tipo del módulo romperían el build al borrarlo.
        /// </summary>
        private static bool IsGeneratedByEntityFramework(string relativePath) =>
            relativePath.StartsWith(MigrationsFolder, StringComparison.Ordinal)
            && (relativePath.EndsWith(".Designer.cs", StringComparison.Ordinal)
                || relativePath.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal));

        /// <summary>Las propiedades públicas del contexto que son un DbSet de una entidad de un módulo.</summary>
        private static string[] ModuleSets(Type contextType) =>
        [
            .. contextType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => IsDbSet(property.PropertyType)
                    && property.PropertyType.GetGenericArguments()[0].Namespace is { } entityNamespace
                    && ModuleNamespaces.ModuleOf(entityNamespace) is not null)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal),
        ];

        /// <summary>
        /// Lo que le falta a <paramref name="module"/>: un registro por cada capa en la que tiene tipos (salvo Domain) y
        /// que Program.cs lo nombre y llame a cada uno.
        /// </summary>
        private static IEnumerable<string> ModuleProblems(
            string module,
            IReadOnlyCollection<string> typeNames,
            IReadOnlyCollection<string> registrations,
            string program)
        {
            var expected = RegisteredLayers
                .Where(layer => typeNames.Any(name =>
                    ModuleNamespaces.ModuleOf(name) == module
                    && name.StartsWith($"ArquitecturaBase.{layer}.", StringComparison.Ordinal)))
                .Select(layer => (
                    Registration: $"ArquitecturaBase.{layer}.Modules.{module}.{module}{layer}Registration.Add{module}{layer}",
                    Call: $".Add{module}{layer}("))
                .ToArray();

            var problems = expected
                .Where(entry => !registrations.Contains(entry.Registration, StringComparer.Ordinal))
                .Select(entry => $"{module}: {entry.Registration} is missing or is not a public static extension")
                .Concat(expected
                    .Where(entry => !program.Contains(entry.Call, StringComparison.Ordinal))
                    .Select(entry => $"{module}: Program.cs does not call {entry.Registration}"));

            // Program.cs es la única excepción de la regla del fuente: si hay un módulo, tiene que nombrarlo.
            return ModuleNames(program).Contains(module, StringComparer.Ordinal)
                ? problems
                : problems.Prepend($"{module}: Program.cs does not name the module");
        }

        /// <summary>
        /// Los registros de módulo de <paramref name="assemblies"/>, como <c>Tipo.Método</c>: una clase
        /// <c>public static</c> <c>ArquitecturaBase.&lt;Capa&gt;.Modules.&lt;M&gt;.&lt;M&gt;&lt;Capa&gt;Registration</c> con
        /// una extensión pública <c>Add&lt;M&gt;&lt;Capa&gt;</c> que recibe y devuelve <see cref="IServiceCollection"/>, así
        /// Program.cs las encadena.
        /// </summary>
        private static string[] Registrations(IEnumerable<Assembly> assemblies) =>
        [
            .. assemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type is { IsPublic: true, IsAbstract: true, IsSealed: true }
                    && ModuleNamespaces.ModuleOf(type.FullName!) is { } module
                    && RegisteredLayers.Any(layer =>
                        type.FullName == $"ArquitecturaBase.{layer}.Modules.{module}.{module}{layer}Registration"))
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(method => method.IsDefined(typeof(ExtensionAttribute), inherit: false)
                        && method.Name == "Add" + type.Name[..^"Registration".Length]
                        && method.ReturnType == typeof(IServiceCollection)
                        && method.GetParameters()[0].ParameterType == typeof(IServiceCollection))
                    .Select(method => $"{type.FullName}.{method.Name}"))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        /// <summary>Los módulos que nombra un texto con su namespace completo, sin repetir.</summary>
        private static IEnumerable<string> ModuleNames(string text) =>
            ModuleName().Matches(text).Select(match => match.Groups["module"].Value).Distinct(StringComparer.Ordinal);

        // El nombre completo de algo de un módulo, en cualquier capa. El punto después de Modules importa (ModulesLegacy
        // no es un módulo). Un nombre parcial, resuelto desde el namespace de quien lo escribe, no se ve: lo ve la prueba
        // de fuego, que compila sin el módulo.
        [GeneratedRegex(@"\bArquitecturaBase\.(Domain|Application|Infrastructure|Api)\.Modules\.(?<module>[A-Z][A-Za-z0-9]*)")]
        private static partial Regex ModuleName();

        [GeneratedRegex(@"^src/ArquitecturaBase\.(Domain|Application|Infrastructure|Api)/Modules/(?<module>[^/]+)/")]
        private static partial Regex ModuleFolder();

        private static bool IsDbSet(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(DbSet<>);

        /// <summary>Los módulos que tienen carpeta en algún proyecto de src.</summary>
        private static IEnumerable<string> ModuleFolders() =>
            Projects
                .Select(project => Path.Combine(SolutionRoot.FullPath, "src", project, "Modules"))
                .Where(Directory.Exists)
                .SelectMany(Directory.EnumerateDirectories)
                .Select(Path.GetFileName)
                .OfType<string>();

        /// <summary>Los .cs y .csproj de los cuatro proyectos, sin bin ni obj, por su ruta desde la raíz.</summary>
        private static Dictionary<string, string> SourceFiles() =>
            Projects
                .Select(project => Path.Combine(SolutionRoot.FullPath, "src", project))
                .SelectMany(directory => Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                    .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".csproj", StringComparison.Ordinal))
                    .Where(path => !IsBuildOutput(directory, path)))
                .ToDictionary(
                    path => Path.GetRelativePath(SolutionRoot.FullPath, path).Replace('\\', '/'),
                    File.ReadAllText,
                    StringComparer.Ordinal);

        private static bool IsBuildOutput(string projectDirectory, string path) =>
            Path.GetRelativePath(projectDirectory, path)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj");
    }

    /// <summary>Un contexto con un conjunto del núcleo y uno de un módulo: el detector tiene que ver solo el segundo.</summary>
    file sealed class ControlContext : DbContext
    {
        public DbSet<UserInvitation> Invitations => Set<UserInvitation>();

        public DbSet<ControlModuleEntity> Entities => Set<ControlModuleEntity>();
    }
}

// Los casos de control de ModuleBoundaryTests, en un módulo inventado. No se ejecutan nunca: solo importan sus tipos.
namespace ArquitecturaBase.Domain.Modules.Control
{
    public sealed class ControlModuleEntity;
}

namespace ArquitecturaBase.Application.Modules.Control
{
    public static class ControlApplicationRegistration
    {
        public static IServiceCollection AddControlApplication(this IServiceCollection services) => services;
    }
}

namespace ArquitecturaBase.Infrastructure.Modules.Control
{
    // El método no se llama AddControlInfrastructure: no cuenta como registro.
    public static class ControlInfrastructureRegistration
    {
        public static IServiceCollection AddControl(this IServiceCollection services) => services;
    }
}

namespace ArquitecturaBase.Api.Modules.Control
{
    // No es pública: Program.cs no la vería desde otro ensamblado, así que no cuenta como registro.
    internal static class ControlApiRegistration
    {
        public static IServiceCollection AddControlApi(this IServiceCollection services) => services;
    }
}
