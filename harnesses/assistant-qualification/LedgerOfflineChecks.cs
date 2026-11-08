using System.Diagnostics;
using System.Text.Json;
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

    // 2026-11-08: a live run stopped when Windows denied replacing the before-submit entry it had just written.
    public static void Ledger_waits_for_a_briefly_held_previous_entry() => HeldEntryAsync().GetAwaiter().GetResult();
    private static async Task HeldEntryAsync()
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"offline-ledger-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var ledger=new SafeRunLedger(new("offline_run",AppContext.BaseDirectory,directory,"unused","unused","unused"));
            var entry=new QualificationLedgerEntry("offline-held",Guid.NewGuid(),Guid.NewGuid(),null,null,"before-submit");
            await ledger.SaveAsync(entry,CancellationToken.None);
            var path=Path.Combine(directory,"ledger","offline-held.json");
            await HeldBrieflyAsync(path,()=>ledger.SaveAsync(entry with{Phase="accepted"},CancellationToken.None));
            var saved=JsonSerializer.Deserialize<QualificationLedgerEntry>(await File.ReadAllBytesAsync(path),JsonSerializerOptions.Web);
            if(saved?.Phase!="accepted"||File.Exists(path+".new"))throw new InvalidOperationException("The accepted phase was not saved.");
        }
        finally{Directory.Delete(directory,true);}
    }

    public static void File_move_waits_for_a_briefly_held_source_and_keeps_write_once() => HeldSourceAsync().GetAwaiter().GetResult();
    private static async Task HeldSourceAsync()
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"offline-move-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var source=Path.Combine(directory,"audio.pending");var target=Path.Combine(directory,"audio.ogg");
            await File.WriteAllTextAsync(source,"first");
            var moved=false;
            await HeldBrieflyAsync(source,async()=>moved=await QualificationFileMoves.MoveAsync(source,target,overwrite:false,CancellationToken.None));
            if(!moved||await File.ReadAllTextAsync(target)!="first")throw new InvalidOperationException("The held source was not moved.");
            await File.WriteAllTextAsync(source,"second");
            var elapsed=Stopwatch.StartNew();
            if(await QualificationFileMoves.MoveAsync(source,target,overwrite:false,CancellationToken.None)
                || await File.ReadAllTextAsync(target)!="first" || elapsed.Elapsed>TimeSpan.FromSeconds(2))
                throw new InvalidOperationException("A written-once object changed or waited for nothing.");
        }
        finally{Directory.Delete(directory,true);}
    }

    // Holds the file without sharing delete, as a scanner or indexer does right after it is closed, and releases it after a moment.
    private static async Task HeldBrieflyAsync(string path,Func<Task> act)
    {
        var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        try
        {
            var action=act();
            await Task.Delay(400);
            await held.DisposeAsync();
            await action;
        }
        catch(Exception exception)when(exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("A briefly held file stopped the harness.",exception);
        }
        finally{await held.DisposeAsync();}
    }
}
