namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
internal static class FormMissingRequestContractCapture
{
    public static async Task WriteAsync(HttpResponseMessage response,string name,CancellationToken ct)
    {
        var path = Environment.GetEnvironmentVariable("FORM_MISSING_REQUEST_HTTP_OUTPUT");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        await File.WriteAllBytesAsync(Path.Combine(path,name + ".json"),await response.Content.ReadAsByteArrayAsync(ct),ct);
    }
}
