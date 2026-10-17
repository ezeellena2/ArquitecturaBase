using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Files;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
namespace AssistantQualification;
internal sealed class QualificationPrivateFiles : IPrivateFileStore
{
    private readonly string directory;
    public QualificationPrivateFiles(string directory){this.directory=Path.GetFullPath(directory);Directory.CreateDirectory(this.directory);}
    public bool IsAvailable=>Directory.Exists(directory);
    public async Task<bool> WriteOnceAsync(string key,Stream content,string contentType,CancellationToken ct)
    {
        var target=Resolve(key);
        if((Path.GetExtension(target),contentType) is not ((".ogg","audio/ogg") or (".wav","audio/wav")))
            throw new InvalidOperationException("Unsupported isolated audio format.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary=target+"."+Guid.NewGuid().ToString("N")+".pending";
        try
        {
            await using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous))
                await content.CopyToAsync(file,ct);
            try{File.Move(temporary,target,overwrite:false);return true;}
            catch(IOException)when(File.Exists(target)){return false;}
        }
        finally{File.Delete(temporary);}
    }
    public async Task WriteAsync(string key,Stream content,string contentType,CancellationToken ct)
    {
        if(!await WriteOnceAsync(key,content,contentType,ct))throw new InvalidOperationException("An isolated qualification object is immutable.");
    }
    public Task<Stream?> OpenReadAsync(string key,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();var target=Resolve(key);
        return Task.FromResult<Stream?>(File.Exists(target)?new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read):null);
    }
    public Task DeleteAsync(string key,CancellationToken ct){ct.ThrowIfCancellationRequested();File.Delete(Resolve(key));return Task.CompletedTask;}
    public async IAsyncEnumerable<PrivateFileObjectRow> ListAsync(DateTime olderThanUtc,[EnumeratorCancellation]CancellationToken ct)
    {
        await Task.CompletedTask;
        foreach(var file in Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();var key=Path.GetRelativePath(directory,file).Replace(Path.DirectorySeparatorChar,'/');
            if(!ValidKey(key))continue;var modified=File.GetLastWriteTimeUtc(file);
            if(modified<olderThanUtc)yield return new(key,modified);
        }
    }
    private string Resolve(string key)
    {
        if(!ValidKey(key))throw new InvalidOperationException("Invalid isolated private audio key.");
        return Path.Combine(directory,key.Replace('/',Path.DirectorySeparatorChar));
    }
    private static bool ValidKey(string key)=>Regex.IsMatch(key,"^qualification/audio/[a-f0-9]{32}\\.(ogg|wav)$",RegexOptions.CultureInvariant);
}
