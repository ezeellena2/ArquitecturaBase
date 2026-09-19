using System.Globalization;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal static class TestEmails
{
    /// <summary>Un email distinto por test, ya normalizado: todos los tests comparten la base.</summary>
    public static string Unique(string prefix) =>
        prefix + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + "@example.com";
}
