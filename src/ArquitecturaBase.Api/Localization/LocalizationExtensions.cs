using Microsoft.AspNetCore.Localization;

namespace ArquitecturaBase.Api.Localization;

internal static class LocalizationExtensions
{
    private static readonly string[] SupportedCultures = ["es", "en"];

    /// <summary>Español por defecto e inglés. El idioma sale de Accept-Language, que envía el front.</summary>
    public static IServiceCollection AddRequestLocalizationDefaults(this IServiceCollection services) =>
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture(SupportedCultures[0])
                .AddSupportedCultures(SupportedCultures)
                .AddSupportedUICultures(SupportedCultures);

            options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
            options.ApplyCurrentCultureToResponseHeaders = true;
        });
}
