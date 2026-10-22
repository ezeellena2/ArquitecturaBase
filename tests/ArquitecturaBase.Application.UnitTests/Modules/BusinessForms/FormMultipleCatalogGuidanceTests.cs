using System.Globalization;
using ArquitecturaBase.Application.Resources;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormMultipleCatalogGuidanceTests
{
    [Theory]
    [InlineData("Forms.Publication.CatalogTooLargeForMultiple", "es", "filtr", "una opci\u00f3n")]
    [InlineData("Forms.Publication.CatalogTooLargeForMultiple", "en", "filter", "single")]
    [InlineData("Forms.Record.CatalogTooLargeForMultiple", "es", "filtr", "una opci\u00f3n")]
    [InlineData("Forms.Record.CatalogTooLargeForMultiple", "en", "filter", "single")]
    public void Oversized_multiple_catalog_errors_explain_the_limit_and_both_recoveries(
        string code, string culture, string filter, string singleChoice)
    {
        var text = ErrorMessages.ResourceManager.GetString(code, CultureInfo.GetCultureInfo(culture));

        Assert.NotNull(text);
        Assert.Contains("20", text, StringComparison.Ordinal);
        Assert.Contains(filter, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(singleChoice, text, StringComparison.OrdinalIgnoreCase);
    }
}
