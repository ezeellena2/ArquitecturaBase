using System.Diagnostics;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms;

/// <summary>
/// F18: the module's traces (its <c>AddSource</c> is in the module's registration), as WhatsApp's H19. An action run and a publication
/// operation keep the W3C context where they were created: the request of the capture or of the publication, or the round of a channel
/// that answered. Their worker continues it, so a response can be followed from the capture to the notice. Tags carry only the
/// run or operation id and the kind of action, never a value of a response, a file name or a phone.
/// </summary>
internal static class FormsTelemetry
{
    public const string SourceName = "ArquitecturaBase.BusinessForms";

    private static readonly ActivitySource Source = new(SourceName);

    /// <summary>The context to keep in a row: only a W3C one fits its column.</summary>
    public static string? Current() => Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;

    public static Activity? StartAction(Guid runId, string kind, string? traceParent)
    {
        var activity = Start("forms.action", traceParent);
        activity?.SetTag("forms.run.id", runId.ToString());
        activity?.SetTag("forms.action.kind", kind);
        return activity;
    }

    public static Activity? StartPublication(Guid operationId, string? traceParent)
    {
        var activity = Start("forms.publication", traceParent);
        activity?.SetTag("forms.publication.id", operationId.ToString());
        return activity;
    }

    /// <summary>A stored context that cannot be read is ignored: the work starts its own trace.</summary>
    private static Activity? Start(string name, string? traceParent) => Source.StartActivity(name, ActivityKind.Consumer,
        ActivityContext.TryParse(traceParent, null, isRemote: true, out var parent) ? parent : default);
}
