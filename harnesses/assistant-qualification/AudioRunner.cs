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
        var bytes=await ReadFixtureAsync(testCase.FileName,testCase.ByteCount,testCase.Sha256,ct);
        var rights=await permissions.GetPermissionsAsync(actor.UserId,ct);
        if(!rights.SequenceEqual([WhatsAppPermissions.Read])) throw new InvalidOperationException("Audio qualification actor must have Read only.");
        var context=QualificationChannelContexts.CreateAudio(actor.UserId,actor.Culture);
        var heard=await NormalizeAsync(testCase.Id,testCase.FileName,testCase.ContentType,bytes,testCase.Sha256,actor,context,"audio",ct);
        var (result,technical)=(heard.Result,heard.Technical);
        var checks=new List<CheckEvidence>
        {
            new("completed",result.State=="completed"),
            new("whole-original-transcript",result.NormalizedAudio is {Text.Length:>0 and <=4000} audio && AudioFixtureRules.Matches(testCase.ExpectedTranscript,audio.Text,testCase.RequiredWords)),
            new("audio-purpose-stamp",technical.Stamps.ProfileId=="openai-stt-candidate" && technical.Stamps.ProfileVersion==1 && technical.Stamps.InstructionsId=="audio-none-v1" && technical.Stamps.InstructionsVersion==1),
            new("real-channel-provenance",technical.Stamps.ChannelId==context.ChannelId && result.Provenance?.ChannelId==context.ChannelId),
            new("single-model-effect",technical.ProviderSteps==1),
            new("no-text-tools-or-proposals",technical.ToolCalls==0 && technical.ToolBytes==0 && technical.Capabilities.Count==0 && result.Proposal is null),
            new("same-saved-connection-revision",heard.SameConnection),
            new("read-only-actor",rights.SequenceEqual([WhatsAppPermissions.Read])),
            new("reported-audio-usage-only",technical.AudioSeconds is null or >=0),
        };
        return new(result,technical,heard.ElapsedSeconds,checks,heard.Measurements);
    }

    // IA3-A by voice: the note arrives in the person's own automatic conversation and is normalized as the app captures it, apart from
    // its interpretation. Its ledger and evidence are "<case>-audio"; Resume reads it back with the same automatic context.
    public async Task<AudioObservation> NormalizeDictationAsync(ChannelCase testCase,QualificationActor author,CancellationToken ct)
    {
        if(testCase.Audio is not {} audio || !AudioFixtureRules.IsValidVoiceNote(audio) || testCase.ActorKind is not ("author" or "sheet-author") || author.Culture!=testCase.Culture)
            throw new InvalidOperationException("Invalid public voice note fixture.");
        var bytes=await ReadFixtureAsync(audio.FileName,audio.ByteCount,audio.Sha256,ct);
        if(!AudioFixtureRules.IsVoiceNote(bytes))throw new InvalidOperationException("Public voice note fixture changed.");
        var heard=await NormalizeAsync(testCase.Id+"-audio",audio.FileName,audio.ContentType,bytes,audio.Sha256,author,
            QualificationChannelContexts.CreateAutomaticAudio(author.UserId,author.Culture),"channel-audio",ct);
        return new(heard.Result,heard.Technical,heard.ElapsedSeconds,
            DictationChecks(audio,heard.Result,heard.Technical,author.UserId,heard.SameConnection),heard.Measurements);
    }

    // The Ogg candidate in the automatic own conversation: whatsapp-assistant, Assistant, the person as Subject, one model effect,
    // nothing offered, and the whole spoken fixture compared in memory. Never exported: fixed IDs and booleans.
    internal static IReadOnlyList<CheckEvidence> DictationChecks(DictationAudioFixture audio,AssistantOperationResponse result,TurnMetadata technical,
        Guid subject,bool sameConnection)=>
    [
        new("completed",result.State=="completed"),
        new("whole-original-transcript",result.NormalizedAudio is {Text.Length:>0 and <=4000} heard && AudioFixtureRules.Matches(audio.ExpectedTranscript,heard.Text,audio.RequiredWords)),
        new("audio-purpose-stamp",technical.Stamps is {ProfileId:"openai-stt-candidate",ProfileVersion:1,InstructionsId:"audio-none-v1",InstructionsVersion:1}),
        new("real-channel-provenance",technical.Stamps.ChannelId=="whatsapp-assistant" && result.Provenance?.ChannelId=="whatsapp-assistant"),
        new("automatic-own-conversation",technical.InteractionMode==AssistantInteractionMode.Assistant && technical.SubjectUserId==subject),
        new("single-model-effect",technical.ProviderSteps==1),
        new("no-text-tools-or-proposals",technical.ToolCalls==0 && technical.ToolBytes==0 && technical.Capabilities.Count==0
            && technical.OfferedCapabilities.Count==0 && result.Proposal is null),
        new("same-saved-connection-revision",sameConnection),
        new("reported-audio-usage-only",technical.AudioSeconds is null or >=0),
    ];

    private static async Task<byte[]> ReadFixtureAsync(string fileName,long byteCount,string sha256,CancellationToken ct)
    {
        var bytes=await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"fixtures","audio",fileName),ct);
        if(bytes.LongLength!=byteCount || EvidenceWriter.HashPublicBytes(bytes)!=sha256)
            throw new InvalidOperationException("Public audio fixture changed.");
        return bytes;
    }

    // One NormalizeAudio operation: the ledger identity fixed before the submit, the public bytes stored WriteOnce under the run, and Get
    // until it ends. A rejection prints only its stable code.
    private async Task<(AssistantOperationResponse Result,TurnMetadata Technical,bool SameConnection,double ElapsedSeconds,MeasurementSnapshot Measurements)> NormalizeAsync(
        string caseId,string fileName,string contentType,byte[] bytes,string sha256,QualificationActor actor,AssistantChannelContext context,string phase,CancellationToken ct)
    {
        var connection=await connections.FindAsync(ct);
        if(!connection.Enabled || connection.Provider.ToString()!="OpenAI" || connection.Model!="gpt-6.1-sol")
            throw new InvalidOperationException("The existing OpenAI connection is unavailable.");
        currentUser.Set(actor.UserId);
        var requestId=Guid.CreateVersion7();
        var entry=new QualificationLedgerEntry(caseId,requestId,actor.UserId,null,null,"before-submit")
            {OperationKind="audio",ChannelId=context.ChannelId,InteractionMode=context.InteractionMode,Culture=context.Culture,SubjectUserId=context.SubjectUserId};
        await ledger.SaveAsync(entry,ct);
        var key="qualification/audio/"+requestId.ToString("N")+Path.GetExtension(fileName);
        using(var input=new MemoryStream(bytes,writable:false))
            if(!await files.WriteOnceAsync(key,input,contentType,ct))
                throw new InvalidOperationException("A qualification object already exists; observe its request instead.");
        measurements.BeginCase();var elapsed=Stopwatch.StartNew();var now=clock.GetUtcNow().UtcDateTime;
        var accepted=await operations.SubmitAsync(new(requestId,context,string.Empty,now,now.AddMinutes(15))
            {Audio=new(key,fileName,contentType,bytes.LongLength,sha256,now.AddMinutes(15))},ct);
        if(accepted.IsFailure)throw QualificationRejections.Stop(phase+"-submit",accepted.Error);
        await ledger.SaveAsync(entry with{Phase="accepted"},ct);
        AssistantOperationResponse? result=null;var deadline=clock.GetUtcNow().AddSeconds(240);
        while(clock.GetUtcNow()<deadline)
        {
            var observed=await operations.GetAsync(context,requestId,ct);
            if(observed.IsFailure)throw QualificationRejections.Stop(phase+"-status",observed.Error);
            result=observed.Value;if(result.State is not ("pending" or "running" or "queued"))break;
            await Task.Delay(TimeSpan.FromSeconds(2),clock,ct);
        }
        if(result is null || result.State is "pending" or "running" or "queued")
            throw new TimeoutException("Retain the accepted audio request identity.");
        var technical=await metadata.FindByRequestAsync(actor.UserId,requestId,ct);
        var current=await connections.FindAsync(ct);
        return (result,technical,connection.Provider==current.Provider && connection.Model==current.Model && connection.Revision==current.Revision,
            elapsed.Elapsed.TotalSeconds,measurements.EndCase());
    }
}
