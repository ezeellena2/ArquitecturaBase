namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// Task 7: intact HTTP bodies of «Documentos recibidos» for the front (cohort forms-documents-http), only with FORM_DOCUMENTS_HTTP_OUTPUT.
internal static class FormReceivedDocumentContractCapture
{
    public static async Task WriteAsync(HttpResponseMessage response, string name, CancellationToken cancellationToken)
    {
        var output = Environment.GetEnvironmentVariable("FORM_DOCUMENTS_HTTP_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, name + ".json"), await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
    }
}
