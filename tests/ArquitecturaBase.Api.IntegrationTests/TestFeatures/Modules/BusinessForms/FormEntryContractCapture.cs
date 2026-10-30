namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

/// <summary>Keeps intact HTTP bodies of the app entry only with FORM_ENTRIES_HTTP_OUTPUT, for the front's contract tests.</summary>
internal static class FormEntryContractCapture
{
    public static async Task WriteAsync(HttpResponseMessage response, string name, CancellationToken cancellationToken)
    {
        var output = Environment.GetEnvironmentVariable("FORM_ENTRIES_HTTP_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, name + ".json"),
            await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
    }
}
