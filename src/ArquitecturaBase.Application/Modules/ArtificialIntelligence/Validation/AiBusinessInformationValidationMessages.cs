using System.Globalization;
using System.Resources;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Validation;
internal static class AiBusinessInformationValidationMessages
{
    internal static ResourceManager ResourceManager { get; } = new("ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources.BusinessInformationValidation", typeof(AiBusinessInformationValidationMessages).Assembly);
    public static string ContentRequired => ResourceManager.GetString(nameof(ContentRequired), CultureInfo.CurrentUICulture)
        ?? throw new InvalidOperationException("The business information validation text is missing.");
    public static string DetailsInvalid => ResourceManager.GetString(nameof(DetailsInvalid), CultureInfo.CurrentUICulture)
        ?? throw new InvalidOperationException("The business information validation text is missing.");
}
