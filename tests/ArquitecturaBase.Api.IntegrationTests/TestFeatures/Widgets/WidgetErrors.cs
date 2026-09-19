using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

public static class WidgetErrors
{
    // Sin traducción en Errors.resx a propósito: la API usa la descripción.
    public static readonly Error NotFound = Error.NotFound("Test.Widget.NotFound", "Widget not found.");
}
