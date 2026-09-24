using ArquitecturaBase.Application.Interfaces.Integrations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace ArquitecturaBase.Api.Routing;

/// <summary>
/// Removes unavailable WhatsApp actions while MVC builds its action descriptors, before routes are mapped.
/// </summary>
public sealed class ConditionalWhatsAppRouteConvention(IWhatsAppAvailability availability) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            for (var index = controller.Actions.Count - 1; index >= 0; index--)
            {
                var action = controller.Actions[index];
                var requirements = controller.Attributes.Concat(action.Attributes).OfType<WhatsAppRouteAttribute>();

                if (requirements.Any(requirement => !IsAvailable(requirement.Feature)))
                {
                    controller.Actions.RemoveAt(index);
                }
            }
        }
    }

    private bool IsAvailable(WhatsAppRouteFeature feature) => feature switch
    {
        WhatsAppRouteFeature.Messaging => availability.IsEnabled,
        WhatsAppRouteFeature.Webhook => availability.IsWebhookEnabled,
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
    };
}

public static class ConditionalWhatsAppRouteRegistration
{
    public static IMvcBuilder AddConditionalWhatsAppRoutes(this IMvcBuilder builder)
    {
        builder.Services.AddOptions<MvcOptions>()
            .Configure<IWhatsAppAvailability>((options, availability) =>
                options.Conventions.Add(new ConditionalWhatsAppRouteConvention(availability)));

        return builder;
    }
}
