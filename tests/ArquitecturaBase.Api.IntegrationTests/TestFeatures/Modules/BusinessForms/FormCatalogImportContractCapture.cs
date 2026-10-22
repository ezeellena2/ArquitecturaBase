using System.Text.Json;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal static class FormCatalogImportContractCapture
{
    public static async Task WriteAsync(HttpResponseMessage response, string culture, string name, CancellationToken ct, bool workbook = false)
    {
        var output = Environment.GetEnvironmentVariable("FORM_CATALOG_IMPORT_HTTP_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var directory = Path.Combine(output, culture); Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, name + (workbook ? ".xlsx" : ".json")), await response.Content.ReadAsByteArrayAsync(ct), ct);
        if (!workbook) return;
        var metadata = new { status = (int)response.StatusCode, contentType = response.Content.Headers.ContentType?.ToString(),
            contentLanguage = response.Content.Headers.ContentLanguage.ToArray(), contentDisposition = response.Content.Headers.ContentDisposition?.ToString() };
        await File.WriteAllBytesAsync(Path.Combine(directory, name + ".http.json"), JsonSerializer.SerializeToUtf8Bytes(metadata), ct);
    }
}
