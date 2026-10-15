using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Files;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Api.IntegrationTests.Files;
public sealed class StoredFileReaderTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Referenced_originals_and_derived_thumbnails_are_found_in_one_key_batch()
    {
        var key = "responses/" + Guid.NewGuid().ToString("N");
        var file = StoredFile.Create(Guid.NewGuid(), "photo.jpg", "image/jpeg", 123, new string('a',64), key, key+".thumb.jpg");
        await factory.ExecuteDbContextAsync(async db => { db.Add(file); await db.SaveChangesAsync(TestContext.Current.CancellationToken); });
        using var scope = factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IStoredFileReader>();
        var result = await reader.ListReferencedKeysAsync([key,key+".thumb.jpg",key+"-missing",key+"-missing.thumb.jpg"],TestContext.Current.CancellationToken);
        Assert.Equal(new[]{key,key+".thumb.jpg"},result.Order(StringComparer.Ordinal).ToArray());
    }
}
