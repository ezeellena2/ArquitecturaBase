using ArquitecturaBase.Domain.Results;

namespace AssistantQualification;

// A rejected engine call stops the run. Only its stable error code is printed, as for the bootstrap: never a validation
// message, a value or content (2026-11-08: a dictated turn was rejected and the run could not say why).
internal static class QualificationRejections
{
    public static InvalidOperationException Stop(string phase, Error error)
    {
        Console.WriteLine("qualification-" + phase + "-error-code: " + error.Code);
        return new InvalidOperationException("Qualification " + phase + " was rejected.");
    }
}
