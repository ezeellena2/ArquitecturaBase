using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// Qué hace la Api con la base al arrancar, según el ambiente (Etapa 7, tarea 9). Sin Docker: los pasos son dobles que
/// anotan lo que se les pidió. El arranque real en Production, contra Postgres, está en ProductionStartupTests.
/// </summary>
public sealed class DatabaseInitializationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Development_validates_the_options_migrates_and_then_seeds()
    {
        var steps = new RecordingSteps(pendingMigrations: ["20260101000000_Initial"]);

        await steps.RunAsync("Development");

        Assert.Equal(["validate-options", "migrate", "seed"], steps.Calls);
    }

    [Fact]
    public async Task Testing_does_nothing_because_the_harness_seeds_after_creating_the_schema()
    {
        var steps = new RecordingSteps(pendingMigrations: ["20260101000000_Initial"]);

        await steps.RunAsync("Testing");

        Assert.Empty(steps.Calls);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_development_a_migrated_database_is_seeded_without_migrating(string environment)
    {
        var steps = new RecordingSteps(pendingMigrations: []);

        await steps.RunAsync(environment);

        Assert.Equal(["validate-options", "list-pending", "seed"], steps.Calls);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Outside_development_pending_migrations_stop_the_start_and_name_the_bundle(string environment)
    {
        var steps = new RecordingSteps(pendingMigrations: ["20260101000000_Initial", "20260202000000_Second"]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => steps.RunAsync(environment));

        Assert.Contains("bundle", exception.Message, StringComparison.Ordinal);
        Assert.Contains("20260202000000_Second", exception.Message, StringComparison.Ordinal);

        // Ni migra por su cuenta ni siembra sobre un esquema viejo.
        Assert.Equal(["validate-options", "list-pending"], steps.Calls);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Invalid_options_stop_the_start_before_touching_the_database(string environment)
    {
        // Lo mismo que haría ValidateOnStart al arrancar el host, pero antes: con la configuración mal escrita no se
        // migra, no se consulta la base ni se siembra, y el error es el de las opciones y no uno de la base.
        var steps = new RecordingSteps(pendingMigrations: [], invalidOptions: true);

        await Assert.ThrowsAsync<OptionsValidationException>(() => steps.RunAsync(environment));

        Assert.Equal(["validate-options"], steps.Calls);
    }

    private sealed class RecordingSteps(IReadOnlyList<string> pendingMigrations, bool invalidOptions = false)
    {
        public List<string> Calls { get; } = [];

        public Task RunAsync(string environment) =>
            DatabaseInitialization.InitializeAsync(
                new TestHostEnvironment { EnvironmentName = environment },
                () =>
                {
                    Calls.Add("validate-options");

                    if (invalidOptions)
                    {
                        throw new OptionsValidationException("Test", typeof(object), ["Invalid test options."]);
                    }
                },
                _ =>
                {
                    Calls.Add("migrate");
                    return Task.CompletedTask;
                },
                _ =>
                {
                    Calls.Add("list-pending");
                    return Task.FromResult<IEnumerable<string>>(pendingMigrations);
                },
                _ =>
                {
                    Calls.Add("seed");
                    return Task.CompletedTask;
                },
                Ct);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
