using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

/// <summary>
/// Qué hace la Api con la base al arrancar, según el ambiente (Etapa 7, tarea 9). Sin Docker: los pasos son dobles que
/// anotan lo que se les pidió. El arranque real en Production, contra Postgres, está en ProductionStartupTests.
/// </summary>
public sealed class DatabaseInitializationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Development_migrates_and_then_seeds()
    {
        var steps = new RecordingSteps(pendingMigrations: ["20260101000000_Initial"]);

        await steps.RunAsync("Development");

        Assert.Equal(["migrate", "seed"], steps.Calls);
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

        Assert.Equal(["list-pending", "seed"], steps.Calls);
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
        Assert.Equal(["list-pending"], steps.Calls);
    }

    private sealed class RecordingSteps(IReadOnlyList<string> pendingMigrations)
    {
        public List<string> Calls { get; } = [];

        public Task RunAsync(string environment) =>
            DatabaseInitialization.InitializeAsync(
                new TestHostEnvironment { EnvironmentName = environment },
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
