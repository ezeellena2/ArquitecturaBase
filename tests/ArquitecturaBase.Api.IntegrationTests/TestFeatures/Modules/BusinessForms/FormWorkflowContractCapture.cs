namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
internal static class FormWorkflowContractCapture
{
    public static async Task WriteAsync(HttpResponseMessage response, string name, CancellationToken cancellationToken)
    {
        var output = Environment.GetEnvironmentVariable("FORM_WORKFLOW_HTTP_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, name + ".json"), await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
    }
}
