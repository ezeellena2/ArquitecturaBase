using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Reading a feed has no effect outside the system (ADR 0018): a short reservation keeps two hosts from asking at once, the
/// source is read between two short transactions and a failure only records why. The first read that works creates the catalog
/// under a lock of its key and files it in the folder the feed names.
/// </summary>
internal sealed class FormCatalogFeedService(IEnumerable<IStructuredCatalogFeed> feeds, IFormCatalogRepository repository, IFormCatalogFeedRepository byKey, IFormCatalogFeedReader reader,
    FormSystemFolderLinker folders, IUnitOfWork unitOfWork, TimeProvider timeProvider, ILogger<FormCatalogFeedService> logger) : IFormCatalogFeedService
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromDays(1);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    public async Task<IReadOnlyList<string>> ListDueAsync(CancellationToken cancellationToken)
    {
        var declared = Declared();
        if (declared.Count == 0) return [];
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stored = (await reader.ListAsync(declared.Keys, cancellationToken)).ToDictionary(row => row.Key, StringComparer.Ordinal);
        return declared.Keys.Where(key => Due(stored.GetValueOrDefault(key), now)).Order(StringComparer.Ordinal).ToArray();
    }

    // A failed read waits 15 minutes; one that worked, a day. A host holding the refresh is left alone until its lease ends.
    private static bool Due(Models.FormCatalogFeedRow? row, DateTime now) => row is null
        || (row.LeaseUntilUtc is null || row.LeaseUntilUtc <= now)
        && (row.FailedAtUtc is { } failed ? failed <= now - RetryAfter : row.RefreshedAtUtc is null || row.RefreshedAtUtc <= now - RefreshEvery);

    public Task<Result<bool>> ReserveAsync(string catalogKey, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "ReserveFormCatalogFeed", () => Declared().ContainsKey(catalogKey)
            ? unitOfWork.ExecuteInTransactionAsync(ct => ReserveCoreAsync(catalogKey, ct), CommitPolicy.OnSuccess, cancellationToken)
            : Task.FromResult(Result.Failure<bool>(FormCatalogErrors.NotFound)));

    public async Task<StructuredFeedReadResponse> ReadAsync(string catalogKey, CancellationToken cancellationToken)
    {
        if (!Declared().TryGetValue(catalogKey, out var entry)) return StructuredFeedReadResponse.Failed(FormCatalogErrors.NotFound.Code);
        try
        {
            return await entry.Feed.ReadAsync(catalogKey, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            OperationLog.Failed(logger, "ReadFormCatalogFeed", exception.GetType().Name);
            return StructuredFeedReadResponse.Failed(FormCatalogErrors.FeedUnavailable.Code);
        }
    }

    public Task<Result> ApplyAsync(string catalogKey, StructuredFeedReadResponse read, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "ApplyFormCatalogFeed", () => Declared().TryGetValue(catalogKey, out var entry)
            ? unitOfWork.ExecuteInTransactionAsync(ct => ApplyCoreAsync(entry.Catalog, read, ct), CommitPolicy.OnSuccess, cancellationToken)
            : Task.FromResult(Result.Failure(FormCatalogErrors.NotFound)));

    // A catalog that does not exist yet is read without a reservation: it is created only when its source answers, so an
    // installation that never connects the module has no empty catalogs. Two hosts reading at once is harmless.
    private async Task<Result<bool>> ReserveCoreAsync(string key, CancellationToken cancellationToken)
    {
        await repository.LockAsync(KeyLock(key), cancellationToken);
        var catalog = await byKey.GetByKeyAsync(key, cancellationToken);
        if (catalog is null) return true;
        if (catalog.Kind != BusinessFormCatalogKind.External) return FormCatalogErrors.ReadOnly;
        return catalog.TryReserveFeed(timeProvider.GetUtcNow().UtcDateTime, Lease);
    }

    private async Task<Result> ApplyCoreAsync(StructuredFeedCatalog declared, StructuredFeedReadResponse read, CancellationToken cancellationToken)
    {
        await repository.LockAsync(KeyLock(declared.Key), cancellationToken);
        var catalog = await byKey.GetByKeyAsync(declared.Key, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (catalog is null)
        {
            if (read.Items is null) return Result.Success();
            catalog = BusinessFormCatalog.CreateExternal(declared.Key, "es", declared.Name, declared.Description, Enum.Parse<BusinessFormCatalogValueType>(declared.ValueType));
            repository.Add(catalog);
            var filed = await folders.FileAsync(FormFolderItemKind.Catalog, catalog.Id, declared.Folder.Key,
                declared.Folder.Name.GetValueOrDefault("es") ?? declared.Folder.Name.Values.First(), cancellationToken);
            if (filed.IsFailure) return filed.Error;
        }
        else if (catalog.Kind != BusinessFormCatalogKind.External) return FormCatalogErrors.ReadOnly;
        else await repository.LockAsync(catalog.Id, cancellationToken);
        if (read.Items is not { } items)
        {
            catalog.RecordFeedFailure(read.FailureCode ?? FormCatalogErrors.FeedUnavailable.Code, now);
            return Result.Success();
        }
        var stored = await repository.ListItemsAsync(catalog.Id, cancellationToken);
        var applied = catalog.ApplyFeed(stored, items.Select(item => new BusinessFormFeedItem(item.Code, Text(item, "es"), Text(item, "en"), item.Value)).ToArray(), now);
        if (applied.IsFailure) catalog.RecordFeedFailure(applied.Error.Code, now);
        else repository.AddItems(applied.Value);
        return Result.Success();
    }

    private Dictionary<string, (IStructuredCatalogFeed Feed, StructuredFeedCatalog Catalog)> Declared()
    {
        var declared = new Dictionary<string, (IStructuredCatalogFeed, StructuredFeedCatalog)>(StringComparer.Ordinal);
        foreach (var feed in feeds)
            foreach (var catalog in feed.Catalogs)
                if (!declared.TryAdd(catalog.Key, (feed, catalog)))
                    throw new InvalidOperationException("Fed catalog keys must be unique across feeds.");
        return declared;
    }

    private static string? Text(StructuredFeedItem item, string culture) =>
        item.Text.TryGetValue(culture, out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    // The catalog does not exist yet: its creation is serialized by a lock derived from its key.
    private static Guid KeyLock(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes("feed:" + key)).AsSpan(0, 16));
}
