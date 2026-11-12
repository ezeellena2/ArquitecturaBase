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
        "AiAssistantCoordinator.cs","EmailBackgroundService.cs","WhatsAppInboundProcessor.cs","FormActionBackgroundService.cs",
        "QualificationAuthoringSeed.cs","QualificationSpreadsheets.cs","AiAssistantFileTurnService.cs","BusinessFormAttachmentReader.cs",
        "Program.Documents.cs","DocumentRunner.cs","QualificationDocuments.cs","FormDocumentReader.cs","AgentRunner.cs","AgentOracle.cs",
        "QualificationAgentSeed.cs","WhatsAppAssistantAgentService.cs","WhatsAppAssistantAgentCapabilitySource.cs","AiBusinessInformationService.cs"];

    internal static readonly string[] CaseSets=["home-existing","channel-stage5","audio-es-en","stage5-all","home-authoring","channel-authoring","ia-authoring","channel-dictation",
        "home-spreadsheets","channel-spreadsheets","ia-spreadsheets","document-reading","channel-agent"];
    // channel-dictation runs only the voice notes of channel-authoring (IA3-A by voice), without repeating the typed cases already qualified.
    internal static ChannelCase[] SelectChannelCases(string? selected,ChannelCase[] cases)=>
        selected=="channel-dictation"?cases.Where(item=>item.Audio is not null).ToArray():cases;
    private const string HomeAuthoringFile="home-authoring-cases.json";
    // IA4: the public candidate for what the browser records in Home, separate from the Ogg/Wav one.
    private static readonly (string File,string Model,string Id) BrowserAudioCandidate=("audio-browser-candidate-profile.json","gpt-transcribe","openai-stt-browser-candidate");
    private const string ChannelAuthoringFile="channel-authoring-cases.json";
    // Stage 2 of «La IA arma formularios y catálogos»: spreadsheets, with their own run and database.
    internal const string HomeSpreadsheetFile="home-spreadsheet-cases.json";
    internal const string ChannelSpreadsheetFile="channel-spreadsheet-cases.json";
    // Casos del asistente (ADR 0021, point 8): the drafted reply and the rest of the agent's menu, on their own run and database.
    internal const string AgentCasesFile="agent-cases.json";
    private static readonly (string File,string Model,string Id) DraftingCandidate=("drafting-candidate-profile.json","gpt-6.1-sol","gpt-6.1-sol-candidate-1");
    // What each context offers, by Id and version: the stamps of its definitions. The five Home reads are version 1.
    internal static readonly AssistantCapabilityReference[] HomeReferences=[..QualificationSeed.HomeCapabilities.Select(id=>new AssistantCapabilityReference(id,1))];
    // Home stamps for the authoring and spreadsheet cases: the five reads plus what BusinessForms offers in Home (authoring and H4), since
    // 6e27fa0b the catalog in version 2 and the import, which the engine leaves out of a turn without a spreadsheet.
    internal static readonly AssistantCapabilityReference[] HomeAuthoringCapabilities=[..HomeReferences,..HomeRunner.SpreadsheetOffered];

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
            var channelAuthoring=selected is "channel-authoring" or "ia-authoring" or "channel-dictation";
            // Stage 2 spreadsheet sets: also their own run and database, with the import catalog seeded only for them.
            var homeSpreadsheets=selected is "home-spreadsheets" or "ia-spreadsheets";
            var channelSpreadsheets=selected is "channel-spreadsheets" or "ia-spreadsheets";
            var spreadsheets=homeSpreadsheets || channelSpreadsheets;
            // Parte L, task 4: ReadDocument alone, on its own run and database seeded only with the base accounts.
            var documents=selected=="document-reading";
            // channel-agent: the base seed, the forms and the agent with its business information, on its own run and database.
            var agentSet=selected=="channel-agent";
            if(!args.Contains("--live",StringComparer.Ordinal))
            {
                if(selected is "home-existing" or "stage5-all")foreach(var item in await CasesAsync<HomeCase>("home-cases.json",CancellationToken.None))Console.WriteLine(item.Id);
                if(selected is "channel-stage5" or "stage5-all")foreach(var item in await CasesAsync<ChannelCase>("channel-cases.json",CancellationToken.None))Console.WriteLine(item.Id);
                if(selected is "audio-es-en" or "stage5-all")foreach(var item in await CasesAsync<AudioCase>("audio-cases.json",CancellationToken.None))Console.WriteLine(item.Id);
                if(homeAuthoring)foreach(var item in await CasesAsync<HomeCase>(HomeAuthoringFile,CancellationToken.None))Console.WriteLine(item.Id);
                if(channelAuthoring)foreach(var item in SelectChannelCases(selected,await CasesAsync<ChannelCase>(ChannelAuthoringFile,CancellationToken.None)))Console.WriteLine(item.Id);
                if(homeSpreadsheets)foreach(var item in await CasesAsync<HomeCase>(HomeSpreadsheetFile,CancellationToken.None))Console.WriteLine(item.Id);
                if(channelSpreadsheets)foreach(var item in await CasesAsync<ChannelCase>(ChannelSpreadsheetFile,CancellationToken.None))Console.WriteLine(item.Id);
                if(documents)foreach(var item in (await QualificationDocuments.LoadVerifiedAsync(CancellationToken.None)).Cases)Console.WriteLine(item.Id);
                if(agentSet)foreach(var item in await CasesAsync<AgentCase>(AgentCasesFile,CancellationToken.None))Console.WriteLine(item.Id);
                Console.WriteLine("Prepared only. No connection or secret store was opened.");return 0;
            }
            // Nothing above this explicit gate reads environment, credentials, services or a database.
            _=RequiredOption(args,"--case-set");
            // A changed document fixture stops a new run here, before its ledger, database, seed or provider call.
            var documentSet=documents && !args.Contains("--resume",StringComparer.Ordinal)?await QualificationDocuments.LoadVerifiedAsync(CancellationToken.None):null;
            var settings=QualificationSettings.FromEnvironment(RequiredOption(args,"--run-id"),RequiredOption(args,"--repository"),RequiredOption(args,"--output"));
            using var timeout=new CancellationTokenSource(TimeSpan.FromHours(2));var ct=timeout.Token;
            if(diagnose)return await DiagnoseExistingFixtureAsync(settings,ct);
            Directory.CreateDirectory(settings.OutputDirectory);
            await SafeRunLedger.BindAsync(settings,continuing || args.Contains("--resume",StringComparer.Ordinal),ct);
            using var host=QualificationHost.Build(settings,agentSet);
            if(args.Contains("--resume",StringComparer.Ordinal))return await ResumeAsync(host,settings,ct);
            if(continuing)
            {
                if(selected!="stage5-all")throw new InvalidOperationException("Existing full runs retain their complete case inventory.");
                return await ContinueExistingAsync(host,settings,ct);
            }
            // Before any seed or provider call: the Home authoring and spreadsheet cases expect what Home offers today, by Id and version.
            if((homeAuthoring || homeSpreadsheets) && !HomeOffersAuthoring(host))return 2;
            QualificationFixture fixture;QualificationChannelFixture? forms=null;QualificationAgentFixture? agent=null;
            await using(var scope=host.Services.CreateAsyncScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await RefuseNonemptyEvaluationDatabaseAsync(db,ct);
                await db.Database.MigrateAsync(ct); // Isolated evaluation DB only.
                fixture=await scope.ServiceProvider.GetRequiredService<QualificationSeed>().CreateAsync(ct);
                if(selected is "channel-stage5" or "stage5-all" || homeAuthoring || channelAuthoring || spreadsheets || agentSet)
                    forms=await scope.ServiceProvider.GetRequiredService<QualificationFormSeed>().CreateAsync(fixture,ct);
                if(agentSet)agent=await scope.ServiceProvider.GetRequiredService<QualificationAgentSeed>().CreateAsync(fixture,forms!,ct);
                if(homeAuthoring || channelAuthoring || spreadsheets)
                {
                    var authoring=scope.ServiceProvider.GetRequiredService<QualificationAuthoringSeed>();
                    fixture=await authoring.CreateAsync(fixture,ct);
                    if(spreadsheets)forms=await authoring.CreateImportCatalogAsync(fixture,forms!,ct);
                }
            }
            await host.StartAsync(ct); // Existing IA worker, never its coordinator or transport.
            try
            {
                if(selected is "home-existing" or "stage5-all")if(!await RunHomeAsync(host,settings,fixture,null,"home-cases.json",HomeReferences,ct))return 1;
                if(homeAuthoring)if(!await RunHomeAsync(host,settings,fixture,forms,HomeAuthoringFile,HomeAuthoringCapabilities,ct))return 1;
                if(selected is "channel-stage5" or "stage5-all")if(!await RunChannelsAsync(host,settings,fixture,forms!,"channel-cases.json",QualificationChannelContexts.References,ct))return 1;
                if(channelAuthoring)if(!await RunChannelsAsync(host,settings,fixture,forms!,ChannelAuthoringFile,QualificationChannelContexts.References,ct,selected))return 1;
                if(selected is "audio-es-en" or "stage5-all")if(!await RunAudioAsync(host,settings,fixture,ct))return 1;
                if(homeSpreadsheets)if(!await RunHomeAsync(host,settings,fixture,forms,HomeSpreadsheetFile,HomeAuthoringCapabilities,ct))return 1;
                if(channelSpreadsheets)if(!await RunChannelsAsync(host,settings,fixture,forms!,ChannelSpreadsheetFile,QualificationChannelContexts.References,ct))return 1;
                if(documentSet is not null)if(!await RunDocumentsAsync(host,settings,fixture,documentSet,ct))return 1;
                if(agent is not null)if(!await RunAgentAsync(host,settings,agent,forms!,ct))return 1;
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
        string fixtureFile,IReadOnlyCollection<AssistantCapabilityReference> capabilities,CancellationToken ct)
    {
        var cases=await CasesAsync<HomeCase>(fixtureFile,ct);
        var definitions=Definitions(host,capabilities);
        // A follow-up keeps its actor, conversation revision and, for an authoring adjustment, the proposal that turn showed and the
        // key of the spreadsheet it sealed (Stage 2), which the follow-up inherits.
        var conversations=new Dictionary<string,(Guid Id,long Revision,QualificationActor Actor,AssistantObservedProposal? Proposal,string? AttachmentKey)>(StringComparer.Ordinal);
        foreach(var testCase in cases)
        {
            await using var scope=host.Services.CreateAsyncScope();
            var actor=HomeActor(cases,testCase,fixture);
            (Guid Id,long Revision)? previous=null;AssistantObservedProposal? shown=null;string? attachmentKey=null;
            if(testCase.ConversationKey is {} key && conversations.TryGetValue(key,out var history))
            {actor=history.Actor;previous=(history.Id,history.Revision);shown=history.Proposal;attachmentKey=history.AttachmentKey;}
            var runner=scope.ServiceProvider.GetRequiredService<HomeRunner>();
            HomeAudioObservation? dictated=null;
            if(testCase.Audio is not null)
            {
                // IA4: the normalization is its own operation and evidence (browser STT candidate); a failed check stops before the turn.
                // Its own scope too, as the browser uploads in a request of its own: in the turn's scope EF would hand back the
                // normalization it tracked when accepting it, still pending, and the turn fails with ContentUnavailable (2026-11-08).
                await using(var audioScope=host.Services.CreateAsyncScope())
                    dictated=await audioScope.ServiceProvider.GetRequiredService<HomeRunner>().NormalizeAsync(testCase,actor,ct);
                var heard=await EvidenceAsync(testCase.Id+"-audio","home-audio",testCase.Culture,dictated.Result.State,dictated.Result.Failure?.Code,
                    dictated.Metadata,dictated.ElapsedSeconds,dictated.Measurements,dictated.Checks,[],fixtureFile,true,ct,BrowserAudioCandidate);
                if(!await SaveAsync(settings,heard,ct))return false;
            }
            var observed=await runner.RunAsync(testCase,actor,previous,ct,fixture,forms,shown,dictated,attachmentKey);
            if(testCase.ConversationKey is {} saved)
                conversations[saved]=(observed.ConversationId,observed.ConversationRevision,actor,observed.Turn.ObservedProposal,observed.Metadata.AttachmentKey);
            var evidence=await EvidenceAsync(testCase.Id,"home",testCase.Culture,observed.Turn.State,observed.Turn.Failure?.Code,
                observed.Metadata,observed.ElapsedSeconds,observed.Measurements,observed.Checks,definitions,fixtureFile,false,ct);
            if(!await SaveAsync(settings,evidence,ct))return false;
        }
        return true;
    }
    // Who asks a Home case: its culture's accounts of its kind, in turns by the case's position (a follow-up keeps its start's).
    internal static QualificationActor HomeActor(HomeCase[] cases,HomeCase testCase,QualificationFixture fixture)
    {
        var pool=(testCase.ActorKind switch{"home"=>fixture.Actors,"author"=>fixture.Authors,_=>[]}).Where(item=>item.Culture==testCase.Culture).ToArray();
        if(pool.Length==0)throw new InvalidOperationException("Unknown Home qualification actor.");
        return pool[Array.IndexOf(cases,testCase)%pool.Length];
    }
    private static async Task<bool> RunChannelsAsync(IHost host,QualificationSettings settings,QualificationFixture fixture,QualificationChannelFixture forms,
        string fixtureFile,IReadOnlyCollection<AssistantCapabilityReference> offered,CancellationToken ct,string? selected=null)
    {
        var definitions=Definitions(host,offered);
        // IA3-A: an adjustment continues the observed operation of its start case (PreviousRequestId), as after «Cambiar algo».
        var observedCases=new Dictionary<string,ChannelObservation>(StringComparer.Ordinal);
        foreach(var testCase in SelectChannelCases(selected,await CasesAsync<ChannelCase>(fixtureFile,ct)))
        {
            AudioObservation? dictated=null;
            if(testCase.Audio is not null)
            {
                // IA3-A by voice: the Ogg candidate normalizes the note in its own scope, as the app does in a round of its own before
                // interpreting the stored transcript (WhatsAppInboundProcessor). A failed check stops before the interpretation.
                await using(var audioScope=host.Services.CreateAsyncScope())
                    dictated=await audioScope.ServiceProvider.GetRequiredService<AudioRunner>()
                        .NormalizeDictationAsync(testCase,ChannelRunner.People(testCase,fixture).Actor,ct);
                var heard=await EvidenceAsync(testCase.Id+"-audio",dictated.Metadata.Stamps.ChannelId,testCase.Culture,dictated.Result.State,dictated.Result.FailureCode,
                    dictated.Metadata,dictated.ElapsedSeconds,dictated.Measurements,dictated.Checks,[],fixtureFile,true,ct);
                if(!await SaveAsync(settings,heard,ct))return false;
            }
            await using var scope=host.Services.CreateAsyncScope();
            var previous=testCase.Authoring?.Continues is {} continues ? observedCases[continues] : null;
            var observed=await scope.ServiceProvider.GetRequiredService<ChannelRunner>().RunAsync(testCase,fixture,forms,ct,previous,dictated);
            observedCases[testCase.Id]=observed;
            var evidence=await EvidenceAsync(testCase.Id,observed.Metadata.Stamps.ChannelId,testCase.Culture,observed.Result.State,observed.Result.FailureCode,
                observed.Metadata,observed.ElapsedSeconds,observed.Measurements,observed.Checks,definitions,fixtureFile,false,ct);
            if(!await SaveAsync(settings,evidence,ct))return false;
        }
        return true;
    }
    // channel-agent: each case in its own scope, in the fixture's order (the drafting cases first); it stops at the first failed check.
    private static async Task<bool> RunAgentAsync(IHost host,QualificationSettings settings,QualificationAgentFixture agent,QualificationChannelFixture forms,CancellationToken ct)
    {
        var definitions=Definitions(host,QualificationChannelContexts.AgentInventory);
        foreach(var testCase in await CasesAsync<AgentCase>(AgentCasesFile,ct))
        {
            await using var scope=host.Services.CreateAsyncScope();
            var observed=await scope.ServiceProvider.GetRequiredService<AgentRunner>().RunAsync(testCase,agent,forms,ct);
            var evidence=await EvidenceAsync(testCase.Id,observed.Metadata.Stamps.ChannelId,testCase.Culture,observed.Result.State,observed.Result.FailureCode,
                observed.Metadata,observed.ElapsedSeconds,observed.Measurements,observed.Checks,definitions,AgentCasesFile,false,ct,DraftingCandidate);
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
    private static CapabilityStamp[] Definitions(IHost host,IReadOnlyCollection<AssistantCapabilityReference> offered)
    {
        using var scope=host.Services.CreateScope();
        return Inventory(scope.ServiceProvider.GetServices<IAssistantCapabilitySource>().SelectMany(source=>source.Definitions),offered);
    }
    // The stamps of what a context offers, by Id and version: each reference names exactly one registered definition, and another
    // version of the same Id (propose-catalog@1 next to @2 since Stage 2) is not part of the context.
    internal static CapabilityStamp[] Inventory(IEnumerable<AssistantCapabilityDefinition> registered,IReadOnlyCollection<AssistantCapabilityReference> offered)
    {
        var wanted=offered.Distinct().OrderBy(item=>item.Id,StringComparer.Ordinal).ThenBy(item=>item.Version).ToArray();
        var definitions=registered.Where(item=>wanted.Contains(new AssistantCapabilityReference(item.Id,item.Version)))
            .OrderBy(item=>item.Id,StringComparer.Ordinal).ThenBy(item=>item.Version)
            .Select(item=>new CapabilityStamp(item.Id,item.Version,EvidenceWriter.HashPublicText(item.InputSchemaJson),EvidenceWriter.HashPublicText(item.OutputSchemaJson))).ToArray();
        if(wanted.Length!=offered.Count || !definitions.Select(item=>new AssistantCapabilityReference(item.Id,item.Version)).SequenceEqual(wanted))
            throw new InvalidOperationException("Qualification capability inventory changed.");
        return definitions;
    }
    // How what Home offers, from what the owners declare OfferedInHome, differs from what the Home authoring and spreadsheet cases expect,
    // by Id and version: a missing reference, or another version of one of their Ids (Home never offers two).
    internal static IReadOnlyList<string> HomeOfferGaps(IEnumerable<AssistantCapabilityDefinition> registered)
    {
        static string Name(AssistantCapabilityReference reference)=>reference.Id+"@"+reference.Version.ToString(CultureInfo.InvariantCulture);
        var expected=HomeRunner.SpreadsheetOffered;
        var offered=registered.Where(item=>item.OfferedInHome).Select(item=>new AssistantCapabilityReference(item.Id,item.Version)).ToArray();
        return [..expected.Where(reference=>!offered.Contains(reference)).Select(reference=>"offer "+Name(reference)),
            ..offered.Where(reference=>!expected.Contains(reference) && expected.Any(item=>item.Id==reference.Id)).Select(reference=>"stop offering "+Name(reference))];
    }
    // Only fixed public capability IDs are printed, before the database is seeded or the provider is called.
    private static bool HomeOffersAuthoring(IHost host)
    {
        using var scope=host.Services.CreateScope();
        var gaps=HomeOfferGaps(scope.ServiceProvider.GetServices<IAssistantCapabilitySource>().SelectMany(source=>source.Definitions));
        foreach(var gap in gaps)Console.Error.WriteLine("qualification-home-offer-differs: Home must "+gap);
        return gaps.Count==0;
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
                    "document"=>(await runner.WaitDocumentAsync(entry,ct))?.State,
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
            ("document-read-v1",1)=>"Prompt.DocumentReadV1", // ReadDocument, in the turn's culture.
            _=>throw new InvalidOperationException("Unknown instruction stamp."),
        };
        return EvidenceWriter.HashPublicText(manager.GetString(key,CultureInfo.GetCultureInfo(culture))??throw new InvalidOperationException("Missing public instructions."));
    }
}
