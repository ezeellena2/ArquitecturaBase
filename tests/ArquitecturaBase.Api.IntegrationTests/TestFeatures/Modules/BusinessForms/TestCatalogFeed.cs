using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

/// <summary>A feed that only exists to test the External catalogs: one catalog, whose items or failure the test sets, counting reads.</summary>
internal sealed class TestCatalogFeed : IStructuredCatalogFeed
{
    public const string Key = "test.feed-colors";
    private int reads;

    public IReadOnlyList<StructuredFeedItem>? Items { get; set; }
    public string? FailureCode { get; set; } = "test.unavailable";
    public int Reads => Volatile.Read(ref reads);

    public IReadOnlyList<StructuredFeedCatalog> Catalogs { get; } =
    [
        new(Key, new Dictionary<string, string> { ["es"] = "Colores", ["en"] = "Colors" }, new Dictionary<string, string> { ["es"] = "Se actualiza solo." },
            "Integer", new StructuredFormStandardFolder("test-feed", new Dictionary<string, string> { ["es"] = "Origen de prueba" })),
    ];

    public Task<StructuredFeedReadResponse> ReadAsync(string catalogKey, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref reads);
        return Task.FromResult(Items is { } items ? new StructuredFeedReadResponse(items) : StructuredFeedReadResponse.Failed(FailureCode!));
    }

    public static StructuredFeedItem Item(string code, string text) => new(code, new Dictionary<string, string> { ["es"] = text }, code);
}
