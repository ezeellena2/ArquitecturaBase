using System.Text.Json;
using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AssistantQualification;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.Ordinal)) return OfflineChecks.Run();
        // Default/list is local-public-files only. Do not initialize configuration, environment or DI before this gate.
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "home-cases.json");
        var document = JsonDocument.Parse(await File.ReadAllBytesAsync(fixturePath));
        var cases = document.RootElement.GetProperty("cases").Deserialize<HomeCase[]>(JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Invalid public qualification fixture.");
        if (!args.Contains("--live", StringComparer.Ordinal))
        {
            foreach (var testCase in cases) Console.WriteLine(testCase.Id);
            Console.WriteLine("Prepared only. No connection or secret store was opened.");
            return 0;
        }
        try
        {
            var runId = RequiredOption(args, "--run-id");
            var repository = RequiredOption(args, "--repository");
            var output = RequiredOption(args, "--output");
            var scope = RequiredOption(args, "--case-set");
            if (scope != "home-existing")
                throw new InvalidOperationException("Full qualification awaits the real C0/WA/H4/H6/H7 operations adapters.");
            var settings = QualificationSettings.FromEnvironment(runId, repository, output);
            Directory.CreateDirectory(settings.OutputDirectory);
            using var timeout = new CancellationTokenSource(TimeSpan.FromHours(2));
            var cancellationToken = timeout.Token;
            await SafeRunLedger.BindAsync(settings, args.Contains("--resume", StringComparer.Ordinal), cancellationToken);
            using var host = QualificationHost.Build(settings);
            if (args.Contains("--resume", StringComparer.Ordinal))
                return await ResumeAsync(host, settings, cancellationToken);
            QualificationFixture fixture;
            CapabilityStamp[] definitions;
            await using (var scopeSeed = host.Services.CreateAsyncScope())
            {
                var db = scopeSeed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await RefuseNonemptyEvaluationDatabaseAsync(db, cancellationToken);
                await db.Database.MigrateAsync(cancellationToken); // Evaluation connection only, never source.
                fixture = await scopeSeed.ServiceProvider.GetRequiredService<QualificationSeed>().CreateAsync(cancellationToken);
                definitions = scopeSeed.ServiceProvider.GetServices<IAssistantCapabilitySource>()
                    .SelectMany(source => source.Definitions).Where(item => QualificationSeed.HomeCapabilities.Contains(item.Id)).OrderBy(item => item.Id, StringComparer.Ordinal)
                    .Select(item => new CapabilityStamp(item.Id, item.Version,
                        EvidenceWriter.HashPublicText(item.InputSchemaJson), EvidenceWriter.HashPublicText(item.OutputSchemaJson))).ToArray();
            }
            var expected = new[] { "business.information", "roles.list", "roles.permissions", "system.help", "users.summary" };
            if (!definitions.Select(item => item.Id).SequenceEqual(expected))
                throw new InvalidOperationException("Home capability inventory changed; rebase qualification fixtures.");
            var profileBytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "candidate-profile.json"), cancellationToken);
            var profileHash = EvidenceWriter.HashPublicBytes(profileBytes);
            var fixtureHash = EvidenceWriter.HashPublicBytes(await File.ReadAllBytesAsync(fixturePath, cancellationToken));
            await host.StartAsync(cancellationToken); // Starts the existing AI BackgroundService; no direct coordinator call.
            var passed = true;
            var conversations = new Dictionary<string, (Guid Id, long Revision, QualificationActor Actor)>(StringComparer.Ordinal);
            try
            {
                foreach (var testCase in cases)
                {
                    await using var scopeCase = host.Services.CreateAsyncScope();
                    var actor = fixture.Actors.Where(item => item.Culture == testCase.Culture)
                        .ElementAt(Array.IndexOf(cases, testCase) % 4);
                    (Guid Id, long Revision)? previous = null;
                    if (testCase.ConversationKey is { } key && conversations.TryGetValue(key, out var history))
                    {
                        actor = history.Actor;
                        previous = (history.Id, history.Revision);
                    }
                    var observed = await scopeCase.ServiceProvider.GetRequiredService<HomeRunner>()
                        .RunAsync(testCase, actor, previous, cancellationToken);
                    if (testCase.ConversationKey is { } savedKey)
                        conversations[savedKey] = (observed.ConversationId, observed.ConversationRevision, actor);
                    var checks = observed.Checks;
                    passed &= checks.All(check => check.Passed);
                    var instructionsHash = HashPublicSystemInstructions(testCase.Culture);
                    var evidence = new CaseEvidence(testCase.Id, "home", testCase.Culture, observed.Turn.State,
                        "gpt-6.1-sol", "gpt-6.1-sol-candidate-1", profileHash,
                        observed.Metadata.Stamps.ProfileId, observed.Metadata.Stamps.ProfileVersion, observed.Metadata.Stamps.ProfileSha256,
                        observed.Metadata.Stamps.InstructionsId, observed.Metadata.Stamps.InstructionsVersion, instructionsHash,
                        definitions.Where(definition => observed.Metadata.Capabilities.Any(capability => capability.Id == definition.Id)).ToArray(),
                        fixtureHash, observed.Metadata.ProviderSteps, observed.Metadata.ToolCalls, observed.Metadata.ToolBytes,
                        new(observed.Metadata.InputTokens, observed.Metadata.OutputTokens, observed.Metadata.UsageUnknown),
                        observed.ElapsedSeconds, observed.Measurements.MaximumContextBytes, observed.Measurements.MaximumContinuationBytes, observed.Turn.Failure?.Code, checks,
                        "candidate; actual-engine-checks; semantic-signoff-pending");
                    await EvidenceWriter.WriteAsync(Path.Combine(settings.OutputDirectory, testCase.Id + ".evidence.json"), evidence, cancellationToken);
                    Console.WriteLine(testCase.Id + ": " + (checks.All(check => check.Passed) ? "mechanical-checks-pass" : "failed"));
                }
            }
            finally { await host.StopAsync(CancellationToken.None); }
            return passed ? 0 : 1; // A zero exit is not a production qualification declaration.
        }
        catch (Exception error)
        {
            // No message, inner exception, configuration, request/result or provider body reaches stdout.
            Console.Error.WriteLine("qualification-aborted: " + error.GetType().Name);
            return 1;
        }
    }

    private static async Task<int> ResumeAsync(IHost host, QualificationSettings settings, CancellationToken cancellationToken)
    {
        var paths = Directory.GetFiles(Path.Combine(settings.OutputDirectory, "ledger"), "*.json");
        if (paths.Length == 0) throw new InvalidOperationException("No existing qualification requests to observe.");
        await host.StartAsync(cancellationToken);
        var complete = true;
        try
        {
            foreach (var path in paths.Order(StringComparer.Ordinal))
            {
                var entry = JsonSerializer.Deserialize<QualificationLedgerEntry>(await File.ReadAllBytesAsync(path, cancellationToken), JsonSerializerOptions.Web)
                    ?? throw new InvalidOperationException("Invalid safe qualification ledger.");
                await using var scope = host.Services.CreateAsyncScope();
                var state = await scope.ServiceProvider.GetRequiredService<StatusRunner>().WaitAsync(entry, cancellationToken);
                complete &= state?.State == "completed";
                Console.WriteLine(entry.CaseId + ": " + (state?.State ?? "not-observable"));
            }
        }
        finally { await host.StopAsync(CancellationToken.None); }
        return complete ? 0 : 2;
    }

    private static string RequiredOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[index + 1] : throw new InvalidOperationException("Required qualification argument is missing.");
    }

    private static async Task RefuseNonemptyEvaluationDatabaseAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        // The evaluation DB is provisioned separately. Never create/drop a database from the source connection.
        await db.Database.OpenConnectionAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public'";
        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        if (count != 0) throw new InvalidOperationException("Evaluation database is not empty; use status recovery instead of reseeding.");
        await db.Database.CloseConnectionAsync();
    }

    private static string HashPublicSystemInstructions(string culture)
    {
        // Only the public static instruction resource is hashed; never the assembled user/context prompt.
        var manager = new ResourceManager("ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources.Assistant",
            typeof(IAiAssistantService).Assembly);
        var value = manager.GetString("Prompt.SystemV1", CultureInfo.GetCultureInfo(culture))
            ?? throw new InvalidOperationException("Public instruction resource is missing.");
        return EvidenceWriter.HashPublicText(value);
    }
}
