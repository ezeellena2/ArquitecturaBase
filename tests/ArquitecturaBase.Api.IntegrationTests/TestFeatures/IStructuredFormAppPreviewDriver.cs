using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

internal interface IStructuredFormAppPreviewDriver
{
    Task<StructuredFormAppPreviewContext> PrepareAsync(ApiFactory factory, Action<IWebHostBuilder> configure,
        IReadOnlyList<string> managerPermissions, byte[] audio, CancellationToken cancellationToken);
}

internal sealed record StructuredFormAppPreviewContext(WebApplicationFactory<Program> Host,
    Guid ManagerId, Guid AuthorId, Guid ReaderId, Guid RestrictedId, Guid ContactId, Guid AudioContactId, Guid AlwaysContactId,
    IAsyncDisposable Lifetime, Func<IReadOnlyList<(string Key, Guid ActorId)>, object, Func<CancellationToken, Task>, CancellationToken, Task> HoldAsync);
