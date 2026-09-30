using Microsoft.AspNetCore.Localization;
using ArquitecturaBase.Application.Interfaces.Services;

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

            options.RequestCultureProviders =
            [
                new AcceptLanguageHeaderRequestCultureProvider(),
                new CustomRequestCultureProvider(async context =>
                {
                    if (!context.Request.Path.StartsWithSegments("/api")
                        && !context.Request.Path.StartsWithSegments("/account")
                        && !context.Request.Path.StartsWithSegments("/connect"))
                    {
                        return null;
                    }

                    var settings = await context.RequestServices.GetRequiredService<ISystemSettingsService>()
                        .GetPresentationAsync(context.RequestAborted);
                    return new ProviderCultureResult(settings.Value.DefaultCulture);
                }),
            ];
            options.ApplyCurrentCultureToResponseHeaders = true;
        });
}
