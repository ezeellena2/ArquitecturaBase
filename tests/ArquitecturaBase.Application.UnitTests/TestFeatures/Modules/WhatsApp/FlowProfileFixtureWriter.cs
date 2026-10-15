namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal static class FlowProfileFixtureWriter
{
    public static void Export(string profile, string culture, string json)
    {
        var directory = Environment.GetEnvironmentVariable("FLOW_PROFILE_FIXTURE_OUTPUT");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, profile + "-" + culture + ".json"), json);
    }
}
