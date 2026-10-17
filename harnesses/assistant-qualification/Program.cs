using System.Globalization;
using System.Resources;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace AssistantQualification;
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if(args.Contains("--self-test",StringComparer.Ordinal))return OfflineChecks.Run();
        try
        {
            var selected=Option(args,"--case-set") ?? "stage5-all";
            if(selected is not ("home-existing" or "channel-stage5" or "audio-es-en" or "stage5-all"))
                throw new InvalidOperationException("Unknown qualification case set.");
            if(!args.Contains("--live",StringComparer.Ordinal))
            {
                if(selected is "home-existing" or "stage5-all")foreach(var item in await CasesAsync<HomeCase>("home-cases.json",CancellationToken.None))Console.WriteLine(item.Id);
                if(selected is "channel-stage5" or "stage5-all")foreach(var item in await CasesAsync<ChannelCase>("channel-cases.json",CancellationToken.None))Console.WriteLine(item.Id);
                if(selected is "audio-es-en" or "stage5-all")foreach(var item in await CasesAsync<AudioCase>("audio-cases.json",CancellationToken.None))Console.WriteLine(item.Id);
                Console.WriteLine("Prepared only. No connection or secret store was opened.");return 0;
            }
            // Nothing above this explicit gate reads environment, credentials, services or a database.
            _=RequiredOption(args,"--case-set");
            var settings=QualificationSettings.FromEnvironment(RequiredOption(args,"--run-id"),RequiredOption(args,"--repository"),RequiredOption(args,"--output"));
            Directory.CreateDirectory(settings.OutputDirectory);using var timeout=new CancellationTokenSource(TimeSpan.FromHours(2));var ct=timeout.Token;
            await SafeRunLedger.BindAsync(settings,args.Contains("--resume",StringComparer.Ordinal),ct);
            using var host=QualificationHost.Build(settings);
            if(args.Contains("--resume",StringComparer.Ordinal))return await ResumeAsync(host,settings,ct);
            QualificationFixture fixture;QualificationChannelFixture? forms=null;
            await using(var scope=host.Services.CreateAsyncScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await RefuseNonemptyEvaluationDatabaseAsync(db,ct);
                await db.Database.MigrateAsync(ct); // Isolated evaluation DB only.
                fixture=await scope.ServiceProvider.GetRequiredService<QualificationSeed>().CreateAsync(ct);
                if(selected is "channel-stage5" or "stage5-all")forms=await scope.ServiceProvider.GetRequiredService<QualificationFormSeed>().CreateAsync(fixture,ct);
            }
            await host.StartAsync(ct); // Existing IA worker, never its coordinator or transport.
            try
            {
                if(selected is "home-existing" or "stage5-all")if(!await RunHomeAsync(host,settings,fixture,ct))return 1;
                if(selected is "channel-stage5" or "stage5-all")if(!await RunChannelsAsync(host,settings,fixture,forms!,ct))return 1;
                if(selected is "audio-es-en" or "stage5-all")if(!await RunAudioAsync(host,settings,fixture,ct))return 1;
                return 0; // Mechanical checks do not declare production qualification.
            }
            finally{await host.StopAsync(CancellationToken.None);}
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("qualification-aborted: "+error.GetType().Name);return 1;
        }
    }
    private static async Task<bool> RunHomeAsync(IHost host,QualificationSettings settings,QualificationFixture fixture,CancellationToken ct)
    {
        var cases=await CasesAsync<HomeCase>("home-cases.json",ct);
        var definitions=Definitions(host,QualificationSeed.HomeCapabilities);
        var conversations=new Dictionary<string,(Guid Id,long Revision,QualificationActor Actor)>(StringComparer.Ordinal);
        foreach(var testCase in cases)
        {
            await using var scope=host.Services.CreateAsyncScope();
            var actor=fixture.Actors.Where(item=>item.Culture==testCase.Culture).ElementAt(Array.IndexOf(cases,testCase)%4);
            (Guid Id,long Revision)? previous=null;
            if(testCase.ConversationKey is {} key && conversations.TryGetValue(key,out var history)){actor=history.Actor;previous=(history.Id,history.Revision);}
            var observed=await scope.ServiceProvider.GetRequiredService<HomeRunner>().RunAsync(testCase,actor,previous,ct);
            if(testCase.ConversationKey is {} saved)conversations[saved]=(observed.ConversationId,observed.ConversationRevision,actor);
            var evidence=await EvidenceAsync(testCase.Id,"home",testCase.Culture,observed.Turn.State,observed.Turn.Failure?.Code,
                observed.Metadata,observed.ElapsedSeconds,observed.Measurements,observed.Checks,definitions,"home-cases.json",false,ct);
            if(!await SaveAsync(settings,evidence,ct))return false;
        }
        return true;
    }
    private static async Task<bool> RunChannelsAsync(IHost host,QualificationSettings settings,QualificationFixture fixture,QualificationChannelFixture forms,CancellationToken ct)
    {
        var definitions=Definitions(host,QualificationChannelContexts.Capabilities);
        foreach(var testCase in await CasesAsync<ChannelCase>("channel-cases.json",ct))
        {
            await using var scope=host.Services.CreateAsyncScope();
            var observed=await scope.ServiceProvider.GetRequiredService<ChannelRunner>().RunAsync(testCase,fixture,forms,ct);
            var evidence=await EvidenceAsync(testCase.Id,observed.Metadata.Stamps.ChannelId,testCase.Culture,observed.Result.State,observed.Result.FailureCode,
                observed.Metadata,observed.ElapsedSeconds,observed.Measurements,observed.Checks,definitions,"channel-cases.json",false,ct);
            if(!await SaveAsync(settings,evidence,ct))return false;
        }
        return true;
    }
    private static async Task<bool> RunAudioAsync(IHost host,QualificationSettings settings,QualificationFixture fixture,CancellationToken ct)
    {
        var cases=await CasesAsync<AudioCase>("audio-cases.json",ct);
        if(cases.Length!=4 || cases.Any(item=>!AudioFixtureRules.IsValid(item)))throw new InvalidOperationException("Audio fixture inventory changed.");
        foreach(var testCase in cases)
        {
            await using var scope=host.Services.CreateAsyncScope();
            var actor=fixture.AudioActors.Single(item=>item.Culture==testCase.Culture);
            var observed=await scope.ServiceProvider.GetRequiredService<AudioRunner>().RunAsync(testCase,actor,ct);
            var evidence=await EvidenceAsync(testCase.Id,"whatsapp-manual",testCase.Culture,observed.Result.State,observed.Result.FailureCode,
                observed.Metadata,observed.ElapsedSeconds,observed.Measurements,observed.Checks,[],"audio-cases.json",true,ct);
            if(!await SaveAsync(settings,evidence,ct))return false;
        }
        return true;
    }
    private static async Task<CaseEvidence> EvidenceAsync(string id,string channel,string culture,string state,string? failure,
        TurnMetadata metadata,double elapsed,MeasurementSnapshot measured,IReadOnlyList<CheckEvidence> checks,CapabilityStamp[] definitions,string fixtureFile,bool audio,CancellationToken ct)
    {
        var profile=audio?"audio-candidate-profile.json":"candidate-profile.json";
        return new(id,channel,culture,state,audio?"gpt-transcribe":"gpt-6.1-sol",audio?"openai-stt-candidate":"gpt-6.1-sol-candidate-1",
            EvidenceWriter.HashPublicBytes(await File.ReadAllBytesAsync(FixturePath(profile),ct)),metadata.Stamps.ProfileId,metadata.Stamps.ProfileVersion,metadata.Stamps.ProfileSha256,
            metadata.Stamps.InstructionsId,metadata.Stamps.InstructionsVersion,HashPublicInstructions(metadata.Stamps.InstructionsId,culture),
            definitions.Where(item=>metadata.Capabilities.Any(actual=>actual.Id==item.Id && actual.Version==item.Version)).ToArray(),
            EvidenceWriter.HashPublicBytes(await File.ReadAllBytesAsync(FixturePath(fixtureFile),ct)),metadata.ProviderSteps,metadata.ToolCalls,metadata.ToolBytes,
            new(metadata.InputTokens,metadata.OutputTokens,metadata.UsageUnknown,metadata.AudioSeconds),elapsed,measured.MaximumContextBytes,measured.MaximumContinuationBytes,
            failure,checks,"fixture-facts-checked-in-memory; style/free-text-not-exhaustively-assessed");
    }
    private static async Task<bool> SaveAsync(QualificationSettings settings,CaseEvidence evidence,CancellationToken ct)
    {
        await EvidenceWriter.WriteAsync(Path.Combine(settings.OutputDirectory,evidence.CaseId+".evidence.json"),evidence,ct);
        var passed=evidence.Checks.All(item=>item.Passed);Console.WriteLine(evidence.CaseId+": "+(passed?"mechanical-checks-pass":"failed"));return passed;
    }
    private static CapabilityStamp[] Definitions(IHost host,IReadOnlyCollection<string> expected)
    {
        using var scope=host.Services.CreateScope();
        var definitions=scope.ServiceProvider.GetServices<IAssistantCapabilitySource>().SelectMany(source=>source.Definitions)
            .Where(item=>expected.Contains(item.Id)).OrderBy(item=>item.Id,StringComparer.Ordinal)
            .Select(item=>new CapabilityStamp(item.Id,item.Version,EvidenceWriter.HashPublicText(item.InputSchemaJson),EvidenceWriter.HashPublicText(item.OutputSchemaJson))).ToArray();
        if(!definitions.Select(item=>item.Id).SequenceEqual(expected.Order(StringComparer.Ordinal)))throw new InvalidOperationException("Qualification capability inventory changed.");
        return definitions;
    }
    private static async Task<int> ResumeAsync(IHost host,QualificationSettings settings,CancellationToken ct)
    {
        var paths=Directory.GetFiles(Path.Combine(settings.OutputDirectory,"ledger"),"*.json");
        if(paths.Length==0)throw new InvalidOperationException("No qualification requests to observe.");
        await host.StartAsync(ct);var complete=true;
        try
        {
            foreach(var path in paths.Order(StringComparer.Ordinal))
            {
                var entry=JsonSerializer.Deserialize<QualificationLedgerEntry>(await File.ReadAllBytesAsync(path,ct),JsonSerializerOptions.Web)
                    ??throw new InvalidOperationException("Invalid qualification ledger.");
                await using var scope=host.Services.CreateAsyncScope();var runner=scope.ServiceProvider.GetRequiredService<StatusRunner>();
                var state=entry.OperationKind=="home"?(await runner.WaitAsync(entry,ct))?.State:(await runner.WaitNeutralAsync(entry,ct))?.State;
                complete &= state=="completed";Console.WriteLine(entry.CaseId+": "+(state??"not-observable"));
            }
        }
        finally{await host.StopAsync(CancellationToken.None);}
        return complete?0:2;
    }
    private static async Task<T[]> CasesAsync<T>(string file,CancellationToken ct)
    {
        using var document=JsonDocument.Parse(await File.ReadAllBytesAsync(FixturePath(file),ct));
        return document.RootElement.GetProperty("cases").Deserialize<T[]>(JsonSerializerOptions.Web)??throw new InvalidOperationException("Invalid public qualification fixture.");
    }
    private static string FixturePath(string file)=>Path.Combine(AppContext.BaseDirectory,"fixtures",file);
    private static string? Option(string[] args,string name)
    {
        var index=Array.IndexOf(args,name);return index>=0 && index+1<args.Length && !args[index+1].StartsWith("--",StringComparison.Ordinal)?args[index+1]:null;
    }
    private static string RequiredOption(string[] args,string name)=>Option(args,name)??throw new InvalidOperationException("Required qualification argument is missing.");
    private static async Task RefuseNonemptyEvaluationDatabaseAsync(ApplicationDbContext db,CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);await using var command=db.Database.GetDbConnection().CreateCommand();
        command.CommandText="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public'";
        if(Convert.ToInt64(await command.ExecuteScalarAsync(ct),CultureInfo.InvariantCulture)!=0)throw new InvalidOperationException("Evaluation database is not empty; use status recovery.");
        await db.Database.CloseConnectionAsync();
    }
    private static string HashPublicInstructions(string id,string culture)
    {
        if(id=="audio-none-v1")return EvidenceWriter.HashPublicText(string.Empty); // NormalizeAudio has no model prompt.
        var manager=new ResourceManager("ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources.Assistant",typeof(IAiAssistantService).Assembly);
        var key=id=="home-system"?"Prompt.SystemV1":id=="capabilities-v1"?"Prompt.CapabilitiesV1":throw new InvalidOperationException("Unknown instruction stamp.");
        return EvidenceWriter.HashPublicText(manager.GetString(key,CultureInfo.GetCultureInfo(culture))??throw new InvalidOperationException("Missing public instructions."));
    }
}
