using ArquitecturaBase.Application.Abstractions.Emails;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Emails;

internal static class EmailRegistration
{
    public static IServiceCollection AddEmails(this IServiceCollection services)
    {
        services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

        return services;
    }
}
