namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal static class FormPersonDataContractCapture
{
    public static async Task WriteAsync(HttpResponseMessage response, string culture, string name, CancellationToken cancellationToken)
    {
        var output = Environment.GetEnvironmentVariable("FORM_PERSON_DATA_HTTP_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, name + "-" + culture + ".json"),
            await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
    }
}
