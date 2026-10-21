using ArquitecturaBase.Application.Models.Assistant;
namespace AssistantQualification;
internal static class LedgerOfflineChecks
{
    public static void Accepted_audio_request_cannot_change_its_identity_or_context() => CheckAsync().GetAwaiter().GetResult();
    private static async Task CheckAsync()
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"offline-ledger-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var ledger=new SafeRunLedger(new("offline_run",AppContext.BaseDirectory,directory,"unused","unused","unused"));
            var entry=new QualificationLedgerEntry("offline-audio",Guid.NewGuid(),Guid.NewGuid(),null,null,"before-submit")
                {OperationKind="audio",ChannelId="whatsapp-manual",Culture="es",InteractionMode=AssistantInteractionMode.Manual};
            await ledger.SaveAsync(entry,CancellationToken.None);await ledger.SaveAsync(entry with{Phase="accepted"},CancellationToken.None);
            var rejected=false;try{await ledger.SaveAsync(entry with{Culture="en"},CancellationToken.None);}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new InvalidOperationException("An accepted operation changed its context.");
            rejected=false;try{await ledger.SaveAsync(entry with{InteractionMode=AssistantInteractionMode.Assistant},CancellationToken.None);}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new InvalidOperationException("An accepted operation changed its interaction mode.");
        }
        finally{Directory.Delete(directory,true);}
    }
}
