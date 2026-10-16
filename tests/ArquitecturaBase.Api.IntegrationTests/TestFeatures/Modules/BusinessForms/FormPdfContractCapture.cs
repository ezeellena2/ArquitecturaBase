using System.Text.Json;
namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
internal static class FormPdfContractCapture
{
    public static async Task WriteAsync(HttpResponseMessage response, string name, CancellationToken cancellationToken, string extension = "json")
    {
        var output = Environment.GetEnvironmentVariable("FORM_PDF_HTTP_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, name + "." + extension), await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
        var metadata = new
        {
            status = (int)response.StatusCode,
            contentType = response.Content.Headers.ContentType?.ToString(),
            contentLanguage = response.Content.Headers.ContentLanguage.ToArray(),
            contentDisposition = response.Content.Headers.ContentDisposition?.ToString(),
        };
        await File.WriteAllBytesAsync(Path.Combine(output, name + ".http.json"), JsonSerializer.SerializeToUtf8Bytes(metadata), cancellationToken);
    }
}
