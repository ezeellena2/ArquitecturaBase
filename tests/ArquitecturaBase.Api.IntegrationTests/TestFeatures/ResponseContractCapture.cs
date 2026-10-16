namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;
internal static class ResponseContractCapture
{
    public static async Task WriteAsync(HttpResponseMessage response, string name, CancellationToken cancellationToken, string extension = "json")
    {
        var output = Environment.GetEnvironmentVariable("FORM_RESPONSES_HTTP_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, name + "." + extension), await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
    }
}
