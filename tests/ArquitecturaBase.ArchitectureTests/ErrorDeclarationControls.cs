using ArquitecturaBase.Domain.Results;

// Casos de control de las reglas de arriba: viven solo en este ensamblado de tests.
namespace ArquitecturaBase.ArchitectureTests
{
    public static class ControlOutsideDomainErrors
{
        public static readonly Error Broken = Error.Failure("Controls.Control.Broken", "Broken.");

        public static Error Invalid() => Error.Validation("Controls.Control.Invalid", "Invalid.");
    }
}

namespace ArquitecturaBase.Domain.Users
{
    public static class ControlUserErrors
    {
        public static readonly Error Broken = Error.Failure("Controls.Control.Broken", "Broken.");
    }

    public static class ControlUserRules
    {
        public static readonly Error Broken = Error.Failure("Controls.Control.Broken", "Broken.");
    }

    public sealed class ControlInstanceErrors
    {
        public static readonly Error Broken = Error.Failure("Controls.Control.Broken", "Broken.");
    }
}

namespace ArquitecturaBase.Domain.Common
{
    public static class ControlCommonErrors
    {
        public static readonly Error Broken = Error.Failure("Controls.Control.Broken", "Broken.");
    }
}
