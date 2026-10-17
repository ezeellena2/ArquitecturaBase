using System.Text;
namespace AssistantQualification;
internal static class PrivateAudioOfflineChecks
{
    public static void Private_audio_is_write_once_and_survives_a_new_adapter() => CheckAsync().GetAwaiter().GetResult();
    private static async Task CheckAsync()
    {
        var directory=Path.Combine(AppContext.BaseDirectory,"offline-audio-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var store=new QualificationPrivateFiles(directory);var key="qualification/audio/"+Guid.NewGuid().ToString("N")+".ogg";
            using var first=new MemoryStream(Encoding.UTF8.GetBytes("public fixture one"));using var second=new MemoryStream(Encoding.UTF8.GetBytes("public fixture two"));
            Require(await store.WriteOnceAsync(key,first,"audio/ogg",CancellationToken.None));
            Require(!await store.WriteOnceAsync(key,second,"audio/ogg",CancellationToken.None));
            using var read=await new QualificationPrivateFiles(directory).OpenReadAsync(key,CancellationToken.None);Require(read is not null);
            using var reader=new StreamReader(read!);Require(await reader.ReadToEndAsync()=="public fixture one");
        }
        finally {Directory.Delete(directory,true);}
    }
    public static void Private_audio_keys_cannot_escape_the_isolated_directory()
    {
        var store=new QualificationPrivateFiles(AppContext.BaseDirectory);var rejected=false;
        try{store.OpenReadAsync("../outside.ogg",CancellationToken.None).GetAwaiter().GetResult();}
        catch(InvalidOperationException){rejected=true;}
        Require(rejected);
    }
    private static void Require(bool valid){if(!valid)throw new InvalidOperationException("Offline private audio check failed.");}
}
