using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.Arca;

public static class ArcaRegistryErrors
{
    public static readonly Error CuitInvalid = Error.Validation("Arca.Registry.CuitInvalid", "The CUIT to look up is not valid.");
    public static readonly Error PersonNotFound = Error.NotFound("Arca.Registry.PersonNotFound", "ARCA has no registry data for that CUIT.");
}
