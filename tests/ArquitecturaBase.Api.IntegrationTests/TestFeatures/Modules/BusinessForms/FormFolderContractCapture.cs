namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// CP1-A to CP5-A: intact HTTP bodies of the folders for the front, under forms-http/folders, only with FORMS_HTTP_CONTRACT_OUTPUT.
internal static class FormFolderContractCapture
{
    public static async Task WriteAsync(HttpResponseMessage response, string name, CancellationToken cancellationToken)
    {
        var output = Environment.GetEnvironmentVariable("FORMS_HTTP_CONTRACT_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var directory = Path.Combine(output, "folders");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, name + ".json"), await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
    }
}
