using System.Reflection;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.ArchitectureTests.Support;
using FluentValidation;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// Cada regla de validación dice un texto del proyecto (Validation/AGENTS.md). Una regla sin <c>WithMessage</c> responde con
/// el texto que trae FluentValidation: en español de España y con el nombre de la propiedad en inglés («'Contact Id' no
/// debería estar vacío.»). La regla se lee del IL, en el orden de la cadena. Cada regla de FluentValidation espera un
/// <c>WithMessage</c> con una función, que lee el texto en el idioma de cada pedido; un texto fijo no cuenta. La espera termina
/// en la regla siguiente, en un helper que agrega otra con su texto (<c>ValidationRules.MaxLength</c>), en el comienzo de otra
/// cadena o en el final del método. <c>Custom</c> escribe sus propios errores, y <c>ChildRules</c>, <c>ForEach</c> y
/// <c>SetInheritanceValidator</c> le pasan el valor a otro validador, que responde con sus reglas. El <c>SetValidator</c> de
/// <c>IRuleBuilder</c> con un validador propio no se mira: trae su texto.
/// </summary>
public sealed class ValidationMessageTests
{
    private const string Rules = "FluentValidation.DefaultValidatorExtensions";
    private const string Options = "FluentValidation.DefaultValidatorOptions";
    private const string Chains = "FluentValidation.AbstractValidator`1";
    private const string RuleOptions = "FluentValidation.IRuleBuilderOptions`2";
    private const string RuleConditions = "FluentValidation.IRuleBuilderOptionsConditions`2";

    private static readonly string[] Delegating = ["ChildRules", "ForEach", "SetInheritanceValidator"];

    // Las sobrecargas de WithMessage que leen el texto al validar: Func<T, string> y Func<T, TProperty, string>.
    private static readonly string[] TextReaders = ["System.Func`2", "System.Func`3"];

    private static readonly Assembly[] Scanned =
        [Assembly.Load("ArquitecturaBase.Application"), Assembly.Load("ArquitecturaBase.Infrastructure"), Assembly.Load("ArquitecturaBase.Api")];

    private static readonly Lazy<Finding[]> InControls = new(() => [.. RulesWithoutMessage(typeof(ValidationMessageTests).Assembly)]);

    private sealed record Finding(string Method, string Rule);

    [Fact]
    public void Every_validation_rule_says_a_project_text()
    {
        var found = Scanned.SelectMany(RulesWithoutMessage).Select(finding => $"{finding.Method}: {finding.Rule}").ToArray();

        Assert.True(found.Length == 0, "Rules without a project text:" + string.Concat(found.Select(rule => Environment.NewLine + rule)));
    }

    [Theory]
    [InlineData(typeof(ControlBeforeAnotherRule), "NotEmpty")]
    [InlineData(typeof(ControlBeforeAnotherChain), "NotEmpty")]
    [InlineData(typeof(ControlAtTheEnd), "InclusiveBetween")]
    [InlineData(typeof(ControlInALambda), "GreaterThan")]
    [InlineData(typeof(ControlBeforeAHelper), "NotNull")]
    [InlineData(typeof(ControlWithAFixedText), "NotEmpty")]
    public void A_rule_without_a_project_text_is_found(Type control, string rule)
    {
        Assert.Equal([rule], RulesOf(control));
    }

    [Fact]
    public void A_rule_with_its_text_is_not_found()
    {
        Assert.Empty(RulesOf(typeof(ControlWithTexts)));
    }

    private static IEnumerable<Finding> RulesWithoutMessage(Assembly assembly) =>
        CallSites.CallsByMethod(assembly).SelectMany(Unanswered);

    // Una regla espera su WithMessage hasta la regla siguiente, lo que devuelva las opciones de otra (un helper, Custom), el
    // comienzo de otra cadena (RuleFor, When y los demás miembros de AbstractValidator) o el final del método. Una opción del
    // medio, como When, no corta la espera.
    private static IEnumerable<Finding> Unanswered(CallSites.MethodCalls method)
    {
        string? waiting = null;
        foreach (var call in method.Calls)
        {
            if (call.DeclaringType == Options)
            {
                if (call.Method == nameof(DefaultValidatorOptions.WithMessage) && TextReaders.Contains(call.Parameters[^1], StringComparer.Ordinal))
                {
                    waiting = null;
                }

                continue;
            }

            var addsRule = call.DeclaringType == Rules && call.ReturnType == RuleOptions
                && !Delegating.Contains(call.Method, StringComparer.Ordinal);
            if (addsRule || call.DeclaringType == Chains || call.ReturnType is RuleOptions or RuleConditions)
            {
                if (waiting is not null)
                {
                    yield return new Finding(method.Method, waiting);
                }

                waiting = addsRule ? call.Method : null;
            }
        }

        if (waiting is not null)
        {
            yield return new Finding(method.Method, waiting);
        }
    }

    // Lo encontrado en un control y en las lambdas que arma, que el compilador deja en el control o en un tipo anidado en él.
    private static string[] RulesOf(Type control)
    {
        var name = control.FullName!.Replace('+', '/');

        return [.. InControls.Value
            .Where(finding => finding.Method.StartsWith(name + "::", StringComparison.Ordinal) || finding.Method.StartsWith(name + "/", StringComparison.Ordinal))
            .Select(finding => finding.Rule)];
    }

    // Casos de control de la regla: viven sólo en este ensamblado de tests y nunca se ejecutan.
    public sealed record ControlSample(string? Name, int Count);

    public sealed class ControlBeforeAnotherRule : AbstractValidator<ControlSample>
    {
        public ControlBeforeAnotherRule() => RuleFor(sample => sample.Name).NotEmpty().MaximumLength(10).WithMessage(_ => "Name");
    }

    public sealed class ControlBeforeAnotherChain : AbstractValidator<ControlSample>
    {
        public ControlBeforeAnotherChain()
        {
            RuleFor(sample => sample.Name).NotEmpty();
            RuleFor(sample => sample.Count).GreaterThan(0).WithMessage(_ => "Count");
        }
    }

    public sealed class ControlAtTheEnd : AbstractValidator<ControlSample>
    {
        public ControlAtTheEnd() => RuleFor(sample => sample.Count).InclusiveBetween(1, 5);
    }

    public sealed class ControlInALambda : AbstractValidator<ControlSample>
    {
        public ControlInALambda() => When(sample => sample.Count > 0, () => RuleFor(sample => sample.Count).GreaterThan(0));
    }

    // MaxLength, de ValidationRules, agrega su regla con su texto: el WithMessage de después es el suyo, no el de NotNull.
    public sealed class ControlBeforeAHelper : AbstractValidator<ControlSample>
    {
        public ControlBeforeAHelper() => RuleFor(sample => sample.Name).NotNull().MaxLength(10).WithMessage(_ => "Name");
    }

    // Un texto fijo no se lee en el idioma del pedido.
    public sealed class ControlWithAFixedText : AbstractValidator<ControlSample>
    {
        public ControlWithAFixedText() => RuleFor(sample => sample.Name).NotEmpty().WithMessage("Name");
    }

    public sealed class ControlWithTexts : AbstractValidator<ControlSample>
    {
        public ControlWithTexts()
        {
            RuleFor(sample => sample.Name).NotEmpty().When(sample => sample.Count > 0).WithMessage(_ => "Name")
                .MaximumLength(10).WithMessage((_, _) => "Name");
            RuleFor(sample => sample.Name).Required().MaxLength(10);
            RuleFor(sample => sample).Custom((_, context) => context.AddFailure("Sample"));
            When(sample => sample.Count > 0, () => RuleFor(sample => sample.Count).GreaterThan(0).WithMessage(_ => "Count"));
        }
    }
}
