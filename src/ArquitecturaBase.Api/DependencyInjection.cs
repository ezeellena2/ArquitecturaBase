using ArquitecturaBase.Api.Endpoints;
using ArquitecturaBase.Api.Localization;
using ArquitecturaBase.Api.Services;
using ArquitecturaBase.Application.Abstractions.Identity;

namespace ArquitecturaBase.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services.AddRequestLocalizationDefaults();
        services.AddOpenApi();

        services.AddEndpoints(typeof(DependencyInjection).Assembly);

        return services;
    }
}
