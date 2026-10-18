using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Files;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
internal sealed class WhatsAppAudioDispatchProbe : IPrivateFileStore,IWhatsAppMediaClient
{
    private readonly Dictionary<string,byte[]> files=[];
    public int Downloads {get;private set;}
    public int Writes {get;private set;}
    public int Reads {get;private set;}
    public bool IsAvailable=>true;
    public byte[] Bytes {get;}=Sample();
    public string Sha256=>Convert.ToHexStringLower(SHA256.HashData(Bytes));
    public static byte[] Sample()
    {var bytes=new byte[47];"OggS"u8.CopyTo(bytes);bytes[26]=1;bytes[27]=19;"OpusHead"u8.CopyTo(bytes.AsSpan(28));bytes[36]=1;bytes[37]=1;return bytes;}
    public Task<Result<WhatsAppMediaContent>> DownloadAsync(string mediaId,string contentType,string sha256,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();Downloads++;Assert.Equal(Sha256,sha256);return Task.FromResult(Result.Success(new WhatsAppMediaContent(Bytes)));}
    public async Task<bool> WriteOnceAsync(string key,Stream content,string contentType,CancellationToken ct)
    {if(files.ContainsKey(key))return false;using var buffer=new MemoryStream();await content.CopyToAsync(buffer,ct);files.Add(key,buffer.ToArray());Writes++;return true;}
    public Task WriteAsync(string key,Stream content,string contentType,CancellationToken ct)=>throw new NotSupportedException();
    public Task<Stream?> OpenReadAsync(string key,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();Reads++;return Task.FromResult<Stream?>(files.TryGetValue(key,out var bytes)?new MemoryStream(bytes,false):null);}
    public Task DeleteAsync(string key,CancellationToken ct){files.Remove(key);return Task.CompletedTask;}
    public async IAsyncEnumerable<PrivateFileObjectRow> ListAsync(DateTime olderThanUtc,[EnumeratorCancellation]CancellationToken cancellationToken)
    {cancellationToken.ThrowIfCancellationRequested();await Task.CompletedTask;yield break;}
}
