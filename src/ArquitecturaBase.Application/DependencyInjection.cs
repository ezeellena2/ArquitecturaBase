using System.Reflection;
using ArquitecturaBase.Application.Abstractions.Behaviors;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.Users;
using ArquitecturaBase.Application.Features.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Settings;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<LoginCodeOptions>()
            .BindConfiguration(LoginCodeOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LoginLinkOptions>()
            .BindConfiguration(LoginLinkOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<WhatsAppLoginOptions>()
            .BindConfiguration(WhatsAppLoginOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.HasValidCountries(), WhatsAppLoginOptions.AllowedCountriesError)
            .ValidateOnStart();

        // No los registra Scrutor: no son handlers. Van acá y no en AddFeaturesFromAssembly, que también corre para
        // el ensamblado de los tests de integración.
        services.AddScoped<UserGuards>();
        services.AddScoped<DestinationCodeVerifier>();
        services.AddScoped<PhoneNumberChange>();
        services.AddScoped<LoginCodeIssuer>();
        services.AddScoped<LoginLinkIssuer>();
        services.AddScoped<AccountCreationPolicy>();
        services.AddScoped<WhatsAppContactLinker>();
        services.AddScoped<UserContactParser>();
        services.AddScoped<UserInvitationSender>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ILoginLinkService, LoginLinkService>();
        services.AddScoped<ISystemSettingsService, SystemSettingsService>();

        services.AddApplicationValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped(typeof(ServiceRequestValidator<>));

        return services.AddFeaturesFromAssembly(typeof(DependencyInjection).Assembly);
    }

    public static IServiceCollection AddApplicationValidatorsFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }

    /// <summary>
    /// Registra los handlers y validadores de un ensamblado y envuelve los handlers con los decoradores
    /// (de afuera hacia adentro: logging → validación → unit of work → handler).
    /// Decora en una colección aparte para no volver a decorar lo que ya estaba registrado; así también
    /// sirve para los handlers que viven en el proyecto de tests de integración.
    /// </summary>
    public static IServiceCollection AddFeaturesFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var features = new FeatureServiceCollection();

        features.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)).Where(IsConcrete), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)).Where(IsConcrete), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)).Where(IsConcrete), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

        // El último decorador aplicado queda afuera. TryDecorate no falla si el ensamblado no tiene handlers.
        features.TryDecorate(typeof(ICommandHandler<>), typeof(UnitOfWorkDecorator.CommandBaseHandler<>));
        features.TryDecorate(typeof(ICommandHandler<,>), typeof(UnitOfWorkDecorator.CommandHandler<,>));

        features.TryDecorate(typeof(ICommandHandler<>), typeof(ValidationDecorator.CommandBaseHandler<>));
        features.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));
        features.TryDecorate(typeof(IQueryHandler<,>), typeof(ValidationDecorator.QueryHandler<,>));

        features.TryDecorate(typeof(ICommandHandler<>), typeof(LoggingDecorator.CommandBaseHandler<>));
        features.TryDecorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator.CommandHandler<,>));
        features.TryDecorate(typeof(IQueryHandler<,>), typeof(LoggingDecorator.QueryHandler<,>));

        foreach (var descriptor in features)
        {
            services.Add(descriptor);
        }

        return services;
    }

    // Excluye los decoradores (genéricos abiertos) que también implementan las interfaces de handler.
    private static bool IsConcrete(Type type) => !type.IsGenericTypeDefinition;

    private sealed class FeatureServiceCollection : List<ServiceDescriptor>, IServiceCollection;
}
