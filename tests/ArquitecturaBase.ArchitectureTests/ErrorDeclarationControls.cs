using ArquitecturaBase.Domain.Results;

// Casos de control de las reglas de arriba: viven solo en este ensamblado de tests.
namespace ArquitecturaBase.ArchitectureTests
{
    public static class ControlOutsideDomainErrors
    {
        public static readonly Error Broken = Error.Failure("Controls.Control.Broken", "Broken.");

        public static readonly Error Built = new("Controls.Control.Built", "Built.", ErrorType.Failure);

        public static readonly Error Copied = Broken with { Code = "Controls.Control.Copied" };

        // Los constructores de ValidationError y la copia with, que armarían un error desde afuera de Domain.
        public static readonly Error Field = new ValidationError("Controls.Control.Field", "Field.", new Dictionary<string, string[]>());

        public static readonly Error Fields = new ValidationError(new Dictionary<string, string[]>());

        public static readonly Error FieldCopied = ((ValidationError)Fields) with { Code = "Controls.Control.Copied" };

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

// En un módulo inventado: el área es el módulo, y debajo de él valen las mismas carpetas que no son un área. La raíz de
// Modules tampoco es un área.
namespace ArquitecturaBase.Domain.Modules.Control
{
    public static class ControlErrors
    {
        public static readonly Error Broken = Error.Failure("Controls.Control.Broken", "Broken.");
    }
}

namespace ArquitecturaBase.Domain.Modules.Control.Common
{
    public static class ControlErrors
    {
        public static readonly Error Broken = Error.Failure("Controls.Control.Broken", "Broken.");
    }
}

namespace ArquitecturaBase.Domain.Modules
{
    public static class ControlModulesRootErrors
    {
        public static readonly Error Broken = Error.Failure("Controls.Control.Broken", "Broken.");
    }
}
