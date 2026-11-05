using System.Globalization;
using System.Diagnostics;
using System.Resources;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
namespace AssistantQualification;
internal static partial class Program
{
    private static readonly string[] DiagnosticSourceFiles=["Program.cs","BusinessFormSource.cs","FormSuggestionRuntimePolicy.cs",
        "FormSuggestionCandidatePolicy.cs","FormSelectionReader.cs","FormSuggestionReader.cs","FormResponseReader.cs",
        "FormResponseReader.Chain.cs","FormRecordReader.cs","FormRecordPresentationPolicy.cs","FormSuggestionMappingPolicy.cs",
        "FormCatalogOptionsPolicy.cs","FormAssistantCandidatePolicy.cs","QualificationFormSeed.cs","QualificationSeed.cs",
        "QualificationHost.cs","QualificationExistingFixtureReader.cs","QualificationContinuationPolicy.cs","Program.Continue.cs","ReadOnlyAiConnectionReader.cs","HomeRunner.cs","AiAssistantService.cs","AiAssistantReadPolicy.cs",
        "AiAssistantAccessGuard.cs","AiAssistantOperations.cs","AiAssistantCapabilityCatalogPolicy.cs","AiAssistantBackgroundService.cs",
        "AiAssistantCoordinator.cs","EmailBackgroundService.cs","WhatsAppInboundProcessor.cs","FormActionBackgroundService.cs"];

    private static readonly string[] CaseSets=["home-existing","channel-stage5","audio-es-en","stage5-all","home-authoring","channel-authoring","ia-authoring"];
    private const string HomeAuthoringFile="home-authoring-cases.json";
    // IA4: the public candidate for what the browser records in Home, separate from the Ogg/Wav one.
    private static readonly (string File,string Model,string Id) BrowserAudioCandidate=("audio-browser-candidate-profile.json","gpt-transcribe","openai-stt-browser-candidate");
    private const string ChannelAuthoringFile="channel-authoring-cases.json";
    // Home stamps for authoring cases: the five reads plus what BusinessForms offers in Home (authoring and H4).
    private static readonly string[] HomeAuthoringCapabilities=[..QualificationSeed.HomeCapabilities,"forms.authoring.catalogs","forms.authoring.forms",
        "forms.authoring.propose-catalog","forms.authoring.propose-form","forms.catalog-filters.metadata","forms.catalog-filters.propose"];

    private static async Task<int> Main(string[] args)
    {
        if(args.Contains("--self-test",StringComparer.Ordinal))return OfflineChecks.Run();
        try
        {
            var diagnose=args.Contains("--diagnose-existing-fixture",StringComparer.Ordinal);
            var continuing=args.Contains("--continue-existing",StringComparer.Ordinal);
            if(continuing && (!args.Contains("--live",StringComparer.Ordinal) || args.Contains("--resume",StringComparer.Ordinal) || diagnose))
                throw new InvalidOperationException("Continuing an existing run requires explicit live mode alone.");
            if(diagnose && (!args.Contains("--live",StringComparer.Ordinal) || args.Contains("--resume",StringComparer.Ordinal)))
                throw new InvalidOperationException("Existing-fixture diagnostics require explicit live mode without resume.");
            var selected=Option(args,"--case-set") ?? "stage5-all";
            if(!CaseSets.Contains(selected,StringComparer.Ordinal))
                throw new InvalidOperationException("Unknown qualification case set.");
            // IA1-A/IA3-A authoring sets run on their own run and database: other quotas, seeded accounts and contracts.
            var homeAuthoring=selected is "home-authoring" or "ia-authoring";
            var channelAuthoring=selected is "channel-authoring" or "ia-authoring";
            if(!args.Contains("--live",StringComparer.Ordinal))
            {
                if(selected is "home-existing" or "stage5-all")foreach(var item in await CasesAsync<HomeCase>("home-cases.json",CancellationToken.None))Console.WriteLine(item.Id);
                if(selected is "channel-stage5" or "stage5-all")foreach(var item in await CasesAsync<ChannelCase>("channel-cases.json",CancellationToken.None))Console.WriteLine(item.Id);
                if(selected is "audio-es-en" or "stage5-all")foreach(var item in await CasesAsync<AudioCase>("audio-cases.json",CancellationToken.None))Console.WriteLine(item.Id);
                if(homeAuthoring)foreach(var item in await CasesAsync<HomeCase>(HomeAuthoringFile,CancellationToken.None))Console.WriteLine(item.Id);
                if(channelAuthoring)foreach(var item in await CasesAsync<ChannelCase>(ChannelAuthoringFile,CancellationToken.None))Console.WriteLine(item.Id);
                Console.WriteLine("Prepared only. No connection or secret store was opened.");return 0;
            }
            // Nothing above this explicit gate reads environment, credentials, services or a database.
            _=RequiredOption(args,"--case-set");
            var settings=QualificationSettings.FromEnvironment(RequiredOption(args,"--run-id"),RequiredOption(args,"--repository"),RequiredOption(args,"--output"));
            using var timeout=new CancellationTokenSource(TimeSpan.FromHours(2));var ct=timeout.Token;
            if(diagnose)return await DiagnoseExistingFixtureAsync(settings,ct);
            Directory.CreateDirectory(settings.OutputDirectory);
            await SafeRunLedger.BindAsync(settings,continuing || args.Contains("--resume",StringComparer.Ordinal),ct);
            using var host=QualificationHost.Build(settings);
            if(args.Contains("--resume",StringComparer.Ordinal))return await ResumeAsync(host,settings,ct);
            if(continuing)
            {
                if(selected!="stage5-all")throw new InvalidOperationException("Existing full runs retain their complete case inventory.");
                return await ContinueExistingAsync(host,settings,ct);
            }
            QualificationFixture fixture;QualificationChannelFixture? forms=null;
            await using(var scope=host.Services.CreateAsyncScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await RefuseNonemptyEvaluationDatabaseAsync(db,ct);
                await db.Database.MigrateAsync(ct); // Isolated evaluation DB only.
                fixture=await scope.ServiceProvider.GetRequiredService<QualificationSeed>().CreateAsync(ct);
                if(selected is "channel-stage5" or "stage5-all" || homeAuthoring || channelAuthoring)
                    forms=await scope.ServiceProvider.GetRequiredService<QualificationFormSeed>().CreateAsync(fixture,ct);
                if(homeAuthoring || channelAuthoring)fixture=await scope.ServiceProvider.GetRequiredService<QualificationAuthoringSeed>().CreateAsync(fixture,ct);
            }
            await host.StartAsync(ct); // Existing IA worker, never its coordinator or transport.
            try
            {
                if(selected is "home-existing" or "stage5-all")if(!await RunHomeAsync(host,settings,fixture,null,"home-cases.json",QualificationSeed.HomeCapabilities,ct))return 1;
                if(homeAuthoring)if(!await RunHomeAsync(host,settings,fixture,forms,HomeAuthoringFile,HomeAuthoringCapabilities,ct))return 1;
                if(selected is "channel-stage5" or "stage5-all")if(!await RunChannelsAsync(host,settings,fixture,forms!,"channel-cases.json",ct))return 1;
                if(channelAuthoring)if(!await RunChannelsAsync(host,settings,fixture,forms!,ChannelAuthoringFile,ct))return 1;
                if(selected is "audio-es-en" or "stage5-all")if(!await RunAudioAsync(host,settings,fixture,ct))return 1;
                return 0; // Mechanical checks do not declare production qualification.
            }
            finally{await host.StopAsync(CancellationToken.None);}
        }
        catch(Exception error)
        {
            ReportFailure(error,false);
            return 1;
        }
    }
    private static async Task<int> DiagnoseExistingFixtureAsync(QualificationSettings settings,CancellationToken ct)
    {
        using var host=QualificationHost.Build(settings.ForReadOnlyDiagnostic());
        await using var scope=host.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY",ct);
        var records=await (from record in db.Set<BusinessFormRecord>().AsNoTracking()
            join user in db.Users.AsNoTracking() on record.ActorUserId equals user.Id
            select new {RecordId=record.Id,record.ActorUserId,user.Culture}).ToArrayAsync(ct);
        Console.WriteLine("fixture-diagnostic-source-count: "+records.Length.ToString(CultureInfo.InvariantCulture));
        if(records.Length==0)return 2;
        var source=records.OrderBy(record=>record.RecordId).First();
        scope.ServiceProvider.GetRequiredService<QualificationCurrentUser>().Set(source.ActorUserId);
        Console.WriteLine("fixture-diagnostic-list-start");
        try
        {
            var listed=await scope.ServiceProvider.GetRequiredService<IStructuredFormSource>()
                .ListSuggestionsAsync(source.ActorUserId,source.Culture,source.RecordId,ct);
            Console.WriteLine("fixture-diagnostic-list-finished: "+listed.IsSuccess);
            if(listed.IsSuccess)Console.WriteLine("fixture-diagnostic-candidate-count: "+listed.Value.Candidates.Count.ToString(CultureInfo.InvariantCulture));
            var validator=host.Services.GetService<IStartupValidator>();
            Console.WriteLine("fixture-diagnostic-startup-validator-present: "+(validator is not null));
            validator?.Validate();
            Console.WriteLine("fixture-diagnostic-startup-options-valid");
            Console.WriteLine("fixture-diagnostic-home-runner-resolve-start");
            _=scope.ServiceProvider.GetRequiredService<HomeRunner>();
            Console.WriteLine("fixture-diagnostic-home-runner-resolve-finished");
            Console.WriteLine("fixture-diagnostic-hosted-services-resolve-start");
            var hostedServices=host.Services.GetServices<IHostedService>().ToArray();
            Console.WriteLine("fixture-diagnostic-hosted-service-count: "+hostedServices.Length.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("fixture-diagnostic-hosted-services-resolve-finished");
            Console.WriteLine("fixture-diagnostic-home-actor-read-start");
            var homeActors=await (from user in db.Users.AsNoTracking()
                join membership in db.UserRoles.AsNoTracking() on user.Id equals membership.UserId
                join role in db.Roles.AsNoTracking() on membership.RoleId equals role.Id
                where role.Name=="QualificationHome" && user.IsActive
                orderby user.Id
                select user.Id).Take(1).ToArrayAsync(ct);
            Console.WriteLine("fixture-diagnostic-home-actor-count: "+homeActors.Length.ToString(CultureInfo.InvariantCulture));
            if(homeActors.Length==0)return 2;
            scope.ServiceProvider.GetRequiredService<QualificationCurrentUser>().Set(homeActors[0]);
            Console.WriteLine("fixture-diagnostic-bootstrap-start");
            var bootstrap=await scope.ServiceProvider.GetRequiredService<IAiAssistantService>().BootstrapAsync(ct);
            Console.WriteLine("fixture-diagnostic-bootstrap-success: "+bootstrap.IsSuccess);
            if(bootstrap.IsSuccess)Console.WriteLine("fixture-diagnostic-bootstrap-availability: "+bootstrap.Value.Availability);
            else Console.WriteLine("fixture-diagnostic-bootstrap-error-code: "+bootstrap.Error.Code);
            var connection=await scope.ServiceProvider.GetRequiredService<IAiConnectionReader>().FindAsync(ct);
            Console.WriteLine("fixture-diagnostic-connection-enabled: "+connection.Enabled);
            Console.WriteLine("fixture-diagnostic-connection-provider: "+connection.Provider);
            Console.WriteLine("fixture-diagnostic-connection-model: "+connection.Model);
            Console.WriteLine("fixture-diagnostic-bootstrap-finished");
            return listed.IsSuccess && bootstrap.IsSuccess && bootstrap.Value.Availability=="available"
                && connection.Enabled && connection.Provider.ToString()=="OpenAI" && connection.Model=="gpt-6.1-sol"?0:2;
        }
        catch(Exception error)
        {
            ReportFailure(error,true);
            return 1;
        }
        // No host.Start, ledger, seed, commit, writer or assistant operation is invoked. Source connection metadata is read only.
    }

    private static void ReportFailure(Exception error,bool diagnostic)
    {
        var prefix=diagnostic?"fixture-diagnostic":"qualification";
        Console.Error.WriteLine(prefix+(diagnostic?"-failed: ":"-aborted: ")+error.GetType().Name);
        IReadOnlyList<Exception> failures=error is AggregateException aggregate?aggregate.Flatten().InnerExceptions:[error];
        foreach(var failure in failures.Prepend(error).Distinct())
        {
            Console.Error.WriteLine(prefix+"-exception-type: "+failure.GetType().Name);
            if(failure is OptionsValidationException invalidOptions)
            {
                Console.Error.WriteLine(prefix+"-options-type: "+invalidOptions.OptionsType.Name);
                Console.Error.WriteLine(prefix+"-option-failure-count: "+invalidOptions.Failures.Count().ToString(CultureInfo.InvariantCulture));
            }
            foreach(var frame in new StackTrace(failure,true).GetFrames())
            {
                var file=Path.GetFileName(frame.GetFileName());
                if(file is not null && DiagnosticSourceFiles.Contains(file,StringComparer.Ordinal) && frame.GetFileLineNumber()>0)
                    Console.Error.WriteLine(prefix+"-location: "+file+":"+frame.GetFileLineNumber().ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private static async Task<bool> RunHomeAsync(IHost host,QualificationSettings settings,QualificationFixture fixture,QualificationChannelFixture? forms,
        string fixtureFile,IReadOnlyCollection<string> capabilities,CancellationToken ct)
    {
        var cases=await CasesAsync<HomeCase>(fixtureFile,ct);
        var definitions=Definitions(host,capabilities);
        // A follow-up keeps its actor, conversation revision and, for an authoring adjustment, the proposal that turn showed.
        var conversations=new Dictionary<string,(Guid Id,long Revision,QualificationActor Actor,AssistantObservedProposal? Proposal)>(StringComparer.Ordinal);
        foreach(var testCase in cases)
        {
            await using var scope=host.Services.CreateAsyncScope();
            var pool=(testCase.ActorKind switch{"home"=>fixture.Actors,"author"=>fixture.Authors,_=>[]}).Where(item=>item.Culture==testCase.Culture).ToArray();
            if(pool.Length==0)throw new InvalidOperationException("Unknown Home qualification actor.");
            var actor=pool[Array.IndexOf(cases,testCase)%pool.Length];
            (Guid Id,long Revision)? previous=null;AssistantObservedProposal? shown=null;
            if(testCase.ConversationKey is {} key && conversations.TryGetValue(key,out var history)){actor=history.Actor;previous=(history.Id,history.Revision);shown=history.Proposal;}
            var runner=scope.ServiceProvider.GetRequiredService<HomeRunner>();
            HomeAudioObservation? dictated=null;
            if(testCase.Audio is not null)
            {
                // IA4: the normalization is its own operation and evidence (browser STT candidate); a failed check stops before the turn.
                dictated=await runner.NormalizeAsync(testCase,actor,ct);
                var heard=await EvidenceAsync(testCase.Id+"-audio","home-audio",testCase.Culture,dictated.Result.State,dictated.Result.Failure?.Code,
                    dictated.Metadata,dictated.ElapsedSeconds,dictated.Measurements,dictated.Checks,[],fixtureFile,true,ct,BrowserAudioCandidate);
                if(!await SaveAsync(settings,heard,ct))return false;
            }
            var observed=await runner.RunAsync(testCase,actor,previous,ct,fixture,forms,shown,dictated);
            if(testCase.ConversationKey is {} saved)conversations[saved]=(observed.ConversationId,observed.ConversationRevision,actor,observed.Turn.ObservedProposal);
            var evidence=await EvidenceAsync(testCase.Id,"home",testCase.Culture,observed.Turn.State,observed.Turn.Failure?.Code,
                observed.Metadata,observed.ElapsedSeconds,observed.Measurements,observed.Checks,definitions,fixtureFile,false,ct);
            if(!await SaveAsync(settings,evidence,ct))return false;
        }
        return true;
    }
    private static async Task<bool> RunChannelsAsync(IHost host,QualificationSettings settings,QualificationFixture fixture,QualificationChannelFixture forms,
        string fixtureFile,CancellationToken ct)
    {
        var definitions=Definitions(host,QualificationChannelContexts.Capabilities);
        // IA3-A: an adjustment continues the observed operation of its start case (PreviousRequestId), as after «Cambiar algo».
        var observedCases=new Dictionary<string,ChannelObservation>(StringComparer.Ordinal);
        foreach(var testCase in await CasesAsync<ChannelCase>(fixtureFile,ct))
        {
            await using var scope=host.Services.CreateAsyncScope();
            var previous=testCase.Authoring?.Continues is {} continues ? observedCases[continues] : null;
            var observed=await scope.ServiceProvider.GetRequiredService<ChannelRunner>().RunAsync(testCase,fixture,forms,ct,previous);
            observedCases[testCase.Id]=observed;
            var evidence=await EvidenceAsync(testCase.Id,observed.Metadata.Stamps.ChannelId,testCase.Culture,observed.Result.State,observed.Result.FailureCode,
                observed.Metadata,observed.ElapsedSeconds,observed.Measurements,observed.Checks,definitions,fixtureFile,false,ct);
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
        TurnMetadata metadata,double elapsed,MeasurementSnapshot measured,IReadOnlyList<CheckEvidence> checks,CapabilityStamp[] definitions,string fixtureFile,bool audio,CancellationToken ct,
        (string File,string Model,string Id)? candidate=null)
    {
        var (profile,model,candidateId)=candidate ?? (audio?("audio-candidate-profile.json","gpt-transcribe","openai-stt-candidate"):("candidate-profile.json","gpt-6.1-sol","gpt-6.1-sol-candidate-1"));
        return new(id,channel,culture,state,model,candidateId,
            EvidenceWriter.HashPublicBytes(await File.ReadAllBytesAsync(FixturePath(profile),ct)),metadata.Stamps.ProfileId,metadata.Stamps.ProfileVersion,metadata.Stamps.ProfileSha256,
            metadata.Stamps.InstructionsId,metadata.Stamps.InstructionsVersion,HashPublicInstructions(metadata.Stamps.InstructionsId,metadata.Stamps.InstructionsVersion,culture),
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
                var state=entry.OperationKind switch
                {
                    "home"=>(await runner.WaitAsync(entry,ct))?.State,
                    "home-audio"=>(await runner.WaitHomeAudioAsync(entry,ct))?.State,
                    _=>(await runner.WaitNeutralAsync(entry,ct))?.State,
                };
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
    internal static string HashPublicInstructions(string id,int version,string culture)
    {
        if(id=="audio-none-v1")return EvidenceWriter.HashPublicText(string.Empty); // NormalizeAudio has no model prompt.
        var manager=new ResourceManager("ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources.Assistant",typeof(IAiAssistantService).Assembly);
        // The sealed Id and version name the exact public prompt: Home turns accepted since IA1-A use home-system@2.
        var key=(id,version) switch
        {
            ("home-system",1)=>"Prompt.SystemV1",
            ("home-system",2)=>"Prompt.SystemV2",
            ("capabilities-v1",1)=>"Prompt.CapabilitiesV1",
            _=>throw new InvalidOperationException("Unknown instruction stamp."),
        };
        return EvidenceWriter.HashPublicText(manager.GetString(key,CultureInfo.GetCultureInfo(culture))??throw new InvalidOperationException("Missing public instructions."));
    }
}
