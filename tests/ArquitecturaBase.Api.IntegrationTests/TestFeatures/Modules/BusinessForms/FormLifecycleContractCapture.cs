namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal static class FormLifecycleContractCapture
{
    public static async Task WriteAsync(HttpResponseMessage response, string culture, string name, CancellationToken cancellationToken)
    {
        var output = Environment.GetEnvironmentVariable("FORM_LIFECYCLE_HTTP_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var directory = Path.Combine(output, culture);
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, name + ".json"),
            await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
    }
}
