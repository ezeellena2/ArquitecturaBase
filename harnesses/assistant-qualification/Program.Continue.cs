using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AssistantQualification;

internal static partial class Program
{
    private static async Task<int> ContinueExistingAsync(IHost host,QualificationSettings settings,CancellationToken ct)
    {
        // A single local continuation owns this run. The existing ledger remains the request authority.
        await using var exclusive=new FileStream(Path.Combine(settings.OutputDirectory,"continuation.lock"),
            FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var home=await CasesAsync<HomeCase>("home-cases.json",ct);
        var channel=await CasesAsync<ChannelCase>("channel-cases.json",ct);
        var audio=await CasesAsync<AudioCase>("audio-cases.json",ct);
        if(audio.Length!=4 || audio.Any(item=>!AudioFixtureRules.IsValid(item)))
            throw new InvalidOperationException("Existing audio fixture inventory changed.");
        var entries=new List<QualificationLedgerEntry>();
        foreach(var path in Directory.GetFiles(Path.Combine(settings.OutputDirectory,"ledger"),"*.json"))
            entries.Add(JsonSerializer.Deserialize<QualificationLedgerEntry>(await File.ReadAllBytesAsync(path,ct),JsonSerializerOptions.Web)
                ??throw new InvalidOperationException("Existing qualification ledger is invalid."));
        var inventory=home.Select(item=>item.Id).Concat(channel.Select(item=>item.Id)).Concat(audio.Select(item=>item.Id)).ToArray();
        var plan=QualificationContinuationPolicy.Build(inventory,entries).ToDictionary(item=>item.CaseId,StringComparer.Ordinal);
        QualificationFixture actors;QualificationChannelFixture forms;
        await using(var scope=host.Services.CreateAsyncScope())
        {
            // Reader uses a read-only transaction. No Migrate, seed, role change, quota reset or source writer.
            var reader=new QualificationExistingFixtureReader(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
            (actors,forms)=await reader.ReadAsync(ct);
        }
        var knownHome=new Dictionary<string,AiAssistantTurnResponse>(StringComparer.Ordinal);
        // Observe EVERY existing request before any new effect, including before-submit uncertainty.
        foreach(var step in plan.Values.Where(item=>!item.ShouldSubmit))
        {
            var entry=step.Existing!;
            await using var scope=host.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<QualificationCurrentUser>().Set(entry.ActorUserId);
            AiAssistantTurnResponse? homeTurn=null;AssistantObservedProposal? channelProposal=null;
            if(entry.OperationKind=="home")
            {
                var read=await scope.ServiceProvider.GetRequiredService<IAiAssistantService>().GetTurnAsync(entry.RequestId,ct);
                if(read.IsFailure || read.Value.State!="completed")
                    throw new InvalidOperationException("Existing Home request cannot be safely continued; observe its original identity.");
                homeTurn=read.Value;
            }
            else
            {
                var context=QualificationChannelContexts.Resume(entry);
                var read=await scope.ServiceProvider.GetRequiredService<IAssistantOperations>().GetAsync(context,entry.RequestId,ct);
                if(read.IsFailure || read.Value.State!="completed")
                    throw new InvalidOperationException("Existing channel request cannot be safely continued; observe its original identity.");
                channelProposal=read.Value.Proposal;
            }
            // Even a successful Get cannot reconstruct lost timing/metric evidence after an interrupted acceptance.
            var evidencePath=Path.Combine(settings.OutputDirectory,entry.CaseId+".evidence.json");
            if(entry.Phase!="accepted" || !File.Exists(evidencePath))
                throw new InvalidOperationException("Existing acceptance needs its original evidence; do not resubmit.");
            var original=JsonSerializer.Deserialize<CaseEvidence>(await File.ReadAllBytesAsync(evidencePath,ct),JsonSerializerOptions.Web)
                ??throw new InvalidOperationException("Existing case evidence is invalid.");
            var profileFile=entry.OperationKind=="audio"?"audio-candidate-profile.json":"candidate-profile.json";
            var fixtureFile=entry.OperationKind=="home"?"home-cases.json":entry.OperationKind=="audio"?"audio-cases.json":"channel-cases.json";
            if(original.CandidateProfileSha256!=EvidenceWriter.HashPublicBytes(await File.ReadAllBytesAsync(FixturePath(profileFile),ct))
                || original.FixtureSha256!=EvidenceWriter.HashPublicBytes(await File.ReadAllBytesAsync(FixturePath(fixtureFile),ct)))
                throw new InvalidOperationException("Existing public qualification profile or fixture changed.");
            var metadata=await scope.ServiceProvider.GetRequiredService<SafeTurnMetadata>().FindByRequestAsync(entry.ActorUserId,entry.RequestId,ct);
            ValidateOriginalEvidence(original,entry,metadata);
            var reevaluated=original with {Assessment="existing-acceptance-read-only; original-metrics-and-checks-preserved"};
            if(homeTurn is not null)
            {
                var testCase=home.Single(item=>item.Id==entry.CaseId);
                if(homeTurn.Culture!=testCase.Culture || homeTurn.Answer is null)
                    throw new InvalidOperationException("Existing Home response scope changed.");
                reevaluated=QualificationContinuationPolicy.ReevaluateHome(original,
                    HomeRunner.FixedFactsMatch(testCase,homeTurn.Answer.Text,homeTurn.Evidences));
                knownHome.Add(entry.CaseId,homeTurn);
            }
            else if(entry.OperationKind=="channel" && entry.CaseId is "channel-original-language-es" or "channel-original-language-en")
            {
                var testCase=channel.Single(item=>item.Id==entry.CaseId);
                if(channelProposal?.Payload is not FormAssistantRegistrationPayload registration || entry.SubjectUserId is not {} subject)
                    throw new InvalidOperationException("Existing optional-choice proposal is not observable.");
                reevaluated=QualificationContinuationPolicy.ReevaluateChannelFieldSet(original,
                    ChannelRunner.ExactRequestedFieldSet(testCase,registration,forms,subject,channelProposal));
            }
            var reevaluatedDirectory=Path.Combine(settings.OutputDirectory,"reevaluated");
            Directory.CreateDirectory(reevaluatedDirectory);
            await EvidenceWriter.WriteAsync(Path.Combine(reevaluatedDirectory,entry.CaseId+".evidence.json"),reevaluated,ct);
            Console.WriteLine(entry.CaseId+": "+(reevaluated.Checks.All(check=>check.Passed)?"existing-checks-pass":"existing-checks-failed"));
            if(reevaluated.Checks.Any(check=>!check.Passed))return 1;
        }
        var homeDefinitions=Definitions(host,QualificationSeed.HomeCapabilities);
        var channelDefinitions=Definitions(host,QualificationChannelContexts.Capabilities);
        var conversations=new Dictionary<string,(Guid Id,long Revision,QualificationActor Actor)>(StringComparer.Ordinal);
        var started=false;
        try
        {
            foreach(var testCase in home)
            {
                var actor=actors.Actors.Where(item=>item.Culture==testCase.Culture).ElementAt(Array.IndexOf(home,testCase)%4);
                (Guid Id,long Revision)? previous=null;
                if(testCase.ConversationKey is {} key && conversations.TryGetValue(key,out var history))
                {actor=history.Actor;previous=(history.Id,history.Revision);}
                if(plan[testCase.Id].Existing is {} entry)
                {
                    var turn=knownHome[testCase.Id];
                    if(entry.ActorUserId!=actor.UserId)throw new InvalidOperationException("Existing Home actor selection changed.");
                    if(testCase.ConversationKey is {} saved)conversations[saved]=(turn.ConversationId,turn.ConversationRevision,actor);
                    continue;
                }
                if(!started){await host.StartAsync(ct);started=true;}
                await using var scope=host.Services.CreateAsyncScope();
                var observed=await scope.ServiceProvider.GetRequiredService<HomeRunner>().RunAsync(testCase,actor,previous,ct);
                if(testCase.ConversationKey is {} savedKey)conversations[savedKey]=(observed.ConversationId,observed.ConversationRevision,actor);
                var evidence=await EvidenceAsync(testCase.Id,"home",testCase.Culture,observed.Turn.State,observed.Turn.Failure?.Code,
                    observed.Metadata,observed.ElapsedSeconds,observed.Measurements,observed.Checks,homeDefinitions,"home-cases.json",false,ct);
                if(!await SaveAsync(settings,evidence,ct))return 1;
            }
            foreach(var testCase in channel)
            {
                if(!plan[testCase.Id].ShouldSubmit)continue;
                if(!started){await host.StartAsync(ct);started=true;}
                await using var scope=host.Services.CreateAsyncScope();
                var observed=await scope.ServiceProvider.GetRequiredService<ChannelRunner>().RunAsync(testCase,actors,forms,ct);
                var evidence=await EvidenceAsync(testCase.Id,observed.Metadata.Stamps.ChannelId,testCase.Culture,observed.Result.State,observed.Result.FailureCode,
                    observed.Metadata,observed.ElapsedSeconds,observed.Measurements,observed.Checks,channelDefinitions,"channel-cases.json",false,ct);
                if(!await SaveAsync(settings,evidence,ct))return 1;
            }
            foreach(var testCase in audio)
            {
                if(!plan[testCase.Id].ShouldSubmit)continue;
                if(!started){await host.StartAsync(ct);started=true;}
                await using var scope=host.Services.CreateAsyncScope();
                var actor=actors.AudioActors.Single(item=>item.Culture==testCase.Culture);
                var observed=await scope.ServiceProvider.GetRequiredService<AudioRunner>().RunAsync(testCase,actor,ct);
                var evidence=await EvidenceAsync(testCase.Id,"whatsapp-manual",testCase.Culture,observed.Result.State,observed.Result.FailureCode,
                    observed.Metadata,observed.ElapsedSeconds,observed.Measurements,observed.Checks,[],"audio-cases.json",true,ct);
                if(!await SaveAsync(settings,evidence,ct))return 1;
            }
            return 0;
        }
        finally{if(started)await host.StopAsync(CancellationToken.None);}
    }

    private static void ValidateOriginalEvidence(CaseEvidence original,QualificationLedgerEntry entry,TurnMetadata metadata)
    {
        var audio=entry.OperationKind=="audio";
        if(original.CaseId!=entry.CaseId || original.State!="completed" || original.Model!=(audio?"gpt-transcribe":"gpt-6.1-sol")
            || original.CandidateProfileId!=(audio?"openai-stt-candidate":"gpt-6.1-sol-candidate-1")
            || original.ExecutionProfileId!=metadata.Stamps.ProfileId || original.ExecutionProfileVersion!=metadata.Stamps.ProfileVersion
            || original.ExecutionProfileVersion!=1 || original.ExecutionProfileSha256!=metadata.Stamps.ProfileSha256
            || original.InstructionsId!=metadata.Stamps.InstructionsId || original.InstructionsVersion!=metadata.Stamps.InstructionsVersion
            || original.ChannelId!=metadata.Stamps.ChannelId || original.ProviderSteps!=metadata.ProviderSteps
            || original.ToolCalls!=metadata.ToolCalls || original.ToolBytes!=metadata.ToolBytes
            || original.Usage!=new UsageEvidence(metadata.InputTokens,metadata.OutputTokens,metadata.UsageUnknown,metadata.AudioSeconds))
            throw new InvalidOperationException("Existing execution evidence does not match its sealed operation.");
    }
}
