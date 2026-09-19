using ArquitecturaBase.Application.Abstractions.Emails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

internal static class EmailRegistration
{
    public static IServiceCollection AddEmails(this IServiceCollection services)
    {
        services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SmtpOptions>()
            .BindConfiguration(SmtpOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SmtpOptions>, SmtpOptionsValidator>();

        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

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
