using System.Diagnostics;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace AssistantQualification;
internal sealed record AudioObservation(AssistantOperationResponse Result,TurnMetadata Metadata,double ElapsedSeconds,
    IReadOnlyList<CheckEvidence> Checks,MeasurementSnapshot Measurements);
internal sealed class AudioRunner(IAssistantOperations operations,IPrivateFileStore files,IPermissionService permissions,
    IAiConnectionReader connections,SafeRunLedger ledger,SafeTurnMetadata metadata,QualificationCurrentUser currentUser,
    TimeProvider clock,QualificationMeasurements measurements)
{
    public async Task<AudioObservation> RunAsync(AudioCase testCase,QualificationActor actor,CancellationToken ct)
    {
        if(!AudioFixtureRules.IsValid(testCase) || actor.Culture!=testCase.Culture)
            throw new InvalidOperationException("Invalid public audio fixture.");
        var bytes=await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"fixtures","audio",testCase.FileName),ct);
        if(bytes.LongLength!=testCase.ByteCount || EvidenceWriter.HashPublicBytes(bytes)!=testCase.Sha256)
            throw new InvalidOperationException("Public audio fixture changed.");
        var connection=await connections.FindAsync(ct);
        if(!connection.Enabled || connection.Provider.ToString()!="OpenAI" || connection.Model!="gpt-6.1-sol")
            throw new InvalidOperationException("The existing OpenAI connection is unavailable.");
        var rights=await permissions.GetPermissionsAsync(actor.UserId,ct);
        if(!rights.SequenceEqual([WhatsAppPermissions.Read])) throw new InvalidOperationException("Audio qualification actor must have Read only.");
        currentUser.Set(actor.UserId);
        var requestId=Guid.CreateVersion7();var context=QualificationChannelContexts.CreateAudio(actor.UserId,actor.Culture);
        var entry=new QualificationLedgerEntry(testCase.Id,requestId,actor.UserId,null,null,"before-submit")
            {OperationKind="audio",ChannelId=context.ChannelId,Culture=context.Culture};
        await ledger.SaveAsync(entry,ct);
        var key="qualification/audio/"+requestId.ToString("N")+Path.GetExtension(testCase.FileName);
        using(var input=new MemoryStream(bytes,writable:false))
            if(!await files.WriteOnceAsync(key,input,testCase.ContentType,ct))
                throw new InvalidOperationException("A qualification object already exists; observe its request instead.");
        measurements.BeginCase();var elapsed=Stopwatch.StartNew();var now=clock.GetUtcNow().UtcDateTime;
        var accepted=await operations.SubmitAsync(new(requestId,context,string.Empty,now,now.AddMinutes(15))
            {Audio=new(key,testCase.FileName,testCase.ContentType,bytes.LongLength,testCase.Sha256,now.AddMinutes(15))},ct);
        if(accepted.IsFailure)throw new InvalidOperationException("Audio qualification submission was rejected.");
        await ledger.SaveAsync(entry with{Phase="accepted"},ct);
        AssistantOperationResponse? result=null;var deadline=clock.GetUtcNow().AddSeconds(240);
        while(clock.GetUtcNow()<deadline)
        {
            var observed=await operations.GetAsync(context,requestId,ct);
            if(observed.IsFailure)throw new InvalidOperationException("Audio qualification status was rejected.");
            result=observed.Value;if(result.State is not ("pending" or "running" or "queued"))break;
            await Task.Delay(TimeSpan.FromSeconds(2),clock,ct);
        }
        if(result is null || result.State is "pending" or "running" or "queued")
            throw new TimeoutException("Retain the accepted audio request identity.");
        var technical=await metadata.FindByRequestAsync(actor.UserId,requestId,ct);
        var currentConnection=await connections.FindAsync(ct);
        var checks=new List<CheckEvidence>
        {
            new("completed",result.State=="completed"),
            new("whole-original-transcript",result.NormalizedAudio is {Text.Length:>0 and <=4000} audio && AudioFixtureRules.Matches(testCase.ExpectedTranscript,audio.Text,testCase.RequiredWords)),
            new("audio-purpose-stamp",technical.Stamps.ProfileId=="openai-stt-candidate" && technical.Stamps.ProfileVersion==1 && technical.Stamps.InstructionsId=="audio-none-v1" && technical.Stamps.InstructionsVersion==1),
            new("real-channel-provenance",technical.Stamps.ChannelId==context.ChannelId && result.Provenance?.ChannelId==context.ChannelId),
            new("single-model-effect",technical.ProviderSteps==1),
            new("no-text-tools-or-proposals",technical.ToolCalls==0 && technical.ToolBytes==0 && technical.Capabilities.Count==0 && result.Proposal is null),
            new("same-saved-connection-revision",connection.Provider==currentConnection.Provider && connection.Model==currentConnection.Model && connection.Revision==currentConnection.Revision),
            new("read-only-actor",rights.SequenceEqual([WhatsAppPermissions.Read])),
            new("reported-audio-usage-only",technical.AudioSeconds is null or >=0),
        };
        return new(result,technical,elapsed.Elapsed.TotalSeconds,checks,measurements.EndCase());
    }
}
