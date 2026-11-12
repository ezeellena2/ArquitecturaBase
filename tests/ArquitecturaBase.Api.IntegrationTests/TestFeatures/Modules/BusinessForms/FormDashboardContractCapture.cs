namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// DOC7-A: intact HTTP bodies of the dashboard for the front (cohort forms-dashboard-http), only with FORM_DASHBOARD_HTTP_OUTPUT.
internal static class FormDashboardContractCapture
{
    public static async Task WriteAsync(HttpResponseMessage response, string name, CancellationToken cancellationToken)
    {
        var output = Environment.GetEnvironmentVariable("FORM_DASHBOARD_HTTP_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, name + ".json"), await response.Content.ReadAsByteArrayAsync(cancellationToken), cancellationToken);
    }
}
