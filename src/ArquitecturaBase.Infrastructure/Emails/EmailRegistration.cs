using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Infrastructure.Identity.OpenIddict;
using ArquitecturaBase.Infrastructure.Delivery;
using ArquitecturaBase.Application.Models.Emails;
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

    /// <summary>Los .eml quedan en el disco de la copia: nadie los recibe, se pierden al desplegar y guardan los códigos.</summary>
    public const string PickupDirectoryOutsideDevelopmentMessage =
        "Email:Delivery is PickupDirectory, which writes the emails as .eml files on the local disk: it is only allowed in "
        + "Development and Testing, because elsewhere nobody would receive them. Set Email:Delivery to Smtp and the "
        + "Email:Smtp keys.";

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
        // arnés. Por lo mismo, los .eml sólo valen ahí (N14): en otro ambiente cada correo quedaría en el disco de la copia
        // y se daría por enviado.
        if (!environment.IsDevelopment() && !environment.IsEnvironment(OpenIddictRegistration.TestingEnvironment))
        {
            services.AddOptions<EmailOptions>()
                .Validate<IPublicOrigin>((_, publicOrigin) => publicOrigin.Value is not null, MissingPublicOriginMessage)
                .Validate(options => options.Delivery == EmailDelivery.Smtp, PickupDirectoryOutsideDevelopmentMessage);
        }

        services.AddOptions<SmtpOptions>()
            .BindConfiguration(SmtpOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SmtpOptions>, SmtpOptionsValidator>();

        // RC6-B: el nombre, el remitente y el logo se leen de la configuración en cada uso, con el lector scoped de los ajustes.
        services.AddScoped<IEmailTemplateRenderer, EmailTemplateRenderer>();

        // El nombre de los correos es el del sistema: lo usan también la invitación por WhatsApp y los PDF.
        services.AddScoped<IAppName, AppName>();
        // N7: los avisos del tope diario del correo, uno por día en cada copia.
        services.AddSingleton<IEmailQuotaWarnings, EmailQuotaWarnings>();

        services.AddScoped<DurableMessageStore>();
        services.AddScoped<EmailQueue>(provider => new EmailQueue(provider.GetRequiredService<IOptions<EmailOptions>>(), provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EmailQueue>>(), provider.GetRequiredService<DurableMessageStore>()));
        services.AddScoped<IEmailQueue>(serviceProvider => serviceProvider.GetRequiredService<EmailQueue>());
        services.AddSingleton<IMessageSource<EmailMessage>, EmailMessageSource>();
        services.AddHostedService<EmailBackgroundService>();

        // N5: readiness y no liveness (sin el tag "live"), en Degraded: un reinicio no arregla una contraseña revocada, y la
        // copia sigue en el tráfico mientras los correos esperan en la cola. Y en qué terminó cada intento, para los tableros.
        services.AddSingleton<EmailHealth>();
        services.AddHealthChecks().AddCheck<EmailHealthCheck>(EmailHealthCheck.Name);
        services.AddMetrics();
        services.AddSingleton<EmailMetrics>();
        services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(EmailMetrics.MeterName));

        // El envío real se elige por configuración: SMTP (Gmail) o archivos .eml en desarrollo.
        services.AddScoped<IEmailSender>(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<EmailOptions>>().Value.Delivery == EmailDelivery.Smtp
                ? ActivatorUtilities.CreateInstance<SmtpEmailSender>(serviceProvider)
                : ActivatorUtilities.CreateInstance<PickupDirectoryEmailSender>(serviceProvider));

        return services;
    }
}
