using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Infrastructure.Identity.OpenIddict;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

internal static class EmailRegistration
{
    /// <summary>Sin un valor: dice qué clave falta, para qué sirve y cuál es la de desarrollo.</summary>
    public const string MissingPublicOriginMessage =
        "Missing Authentication:Issuer, the public origin of the web app (https://localhost:5173/ in development): "
        + "invitations by email link to its login page. Set it to the address that the browser sees, not the one of "
        + "the Api behind the proxy.";

    public static IServiceCollection AddEmails(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // La invitación por correo lleva un botón a la web, y la dirección de la web es el origen público. Se manda desde
        // cualquier despliegue, con el webhook de WhatsApp o sin él: sin la dirección, fallaría recién la primera, y con
        // ella el alta que la pedía. Mejor que la Api no arranque y diga qué falta. Fuera de Development y Testing, como
        // los certificados de OpenIddict: en Development la trae appsettings.Development.json, y en Testing la pone el
        // arnés.
        if (!environment.IsDevelopment() && !environment.IsEnvironment(OpenIddictRegistration.TestingEnvironment))
        {
            services.AddOptions<EmailOptions>()
                .Validate<IPublicOrigin>((_, publicOrigin) => publicOrigin.Value is not null, MissingPublicOriginMessage);
        }

        services.AddOptions<SmtpOptions>()
            .BindConfiguration(SmtpOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SmtpOptions>, SmtpOptionsValidator>();

        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

        // El nombre de los correos es el del sistema: lo usan también los mensajes del bot de WhatsApp.
        services.AddSingleton<IAppName, AppName>();

        services.AddSingleton<EmailQueue>();
        services.AddSingleton<IEmailQueue>(serviceProvider => serviceProvider.GetRequiredService<EmailQueue>());
        services.AddHostedService<EmailBackgroundService>();

        // El envío real se elige por configuración: SMTP (Gmail) o archivos .eml en desarrollo.
        services.AddScoped<IEmailSender>(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<EmailOptions>>().Value.Delivery == EmailDelivery.Smtp
                ? ActivatorUtilities.CreateInstance<SmtpEmailSender>(serviceProvider)
                : ActivatorUtilities.CreateInstance<PickupDirectoryEmailSender>(serviceProvider));

        return services;
    }
}
