namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// Bytes HTTP intactos de catálogos para la cohorte catalogs-http/manual-current, sólo con FORMS_CATALOG_HTTP_CONTRACT_OUTPUT.
internal static class FormCatalogContractCapture
{
    public static async Task WriteAsync(HttpResponseMessage response, string name, CancellationToken cancellationToken)
    {
        var output = Environment.GetEnvironmentVariable("FORMS_CATALOG_HTTP_CONTRACT_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var directory = Path.Combine(output, "manual-bilingual");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, name + ".json"),
            await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
    }
}
