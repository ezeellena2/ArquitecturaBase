using System.Reflection;
using System.Runtime.CompilerServices;
using ArquitecturaBase.Api.Contracts.Auth;
using ArquitecturaBase.Api.Contracts.Users;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class ControllerInputContractTests
{
    private const string ContractsNamespace = "ArquitecturaBase.Api.Contracts";

    private static readonly Assembly ApiAssembly = Assembly.Load("ArquitecturaBase.Api");

    // Los valores sueltos que MVC arma desde un texto: un id, un número de página, un filtro o un enum.
    private static readonly Type[] SimpleValueTypes =
    [
        typeof(string), typeof(decimal), typeof(Guid), typeof(DateTime), typeof(DateTimeOffset), typeof(DateOnly),
        typeof(TimeOnly), typeof(TimeSpan),
    ];

    // Cómo se llama en un contrato lo que no puede quedar en un log (backend.md, "Borde HTTP"): el correo, el teléfono, un
    // código, un token o una clave de API y el nombre del perfil. Cuenta el final del nombre, así CheckToken es un token.
    private static readonly string[] PrivateDataNames = ["Email", "Phone", "Code", "Token", "ApiKey", "DisplayName"];

    // El número de teléfono que se escribe al lado del país se llama así, a secas: VersionNumber no es un teléfono.
    private const string PhoneNumber = "Number";

    [Fact]
    public void Controller_body_query_and_form_parameters_are_http_contracts()
    {
        // La entrada de una acción es un contrato de Api/Contracts, mapeado a mano al modelo de Application: así el
        // JSON que acepta la Api no cambia sin querer al tocar un modelo, y lo que MVC registra de los argumentos
        // (su ToString()) lo decide la Api, no Application.
        var controllers = ApiAssembly.GetTypes().Where(IsController).ToArray();
        var inputs = controllers.SelectMany(ActionInputs).ToArray();

        // Si la búsqueda dejara de encontrar los parámetros, la regla de abajo pasaría en silencio. Sólo se piden los
        // del núcleo: el único [FromForm] es de un módulo, y que la búsqueda lo encuentra lo prueba ProbeController.
        Assert.Contains(inputs, input => input.Source == BindingSource.Body);
        Assert.Contains(inputs, input => input.Source == BindingSource.Query);

        var offenders = inputs
            .Where(input => !IsAllowedInput(input.Parameter.ParameterType))
            .Select(input => $"{input.Action}({input.Parameter.ParameterType.FullName} {input.Parameter.Name})")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Bind a type from {ContractsNamespace} and map it to the application model: "
            + string.Join(", ", offenders));
    }

    // La regla de arriba hoy pasa; esto comprueba que la detección de verdad encuentra un modelo de Application cuando
    // llega por el cuerpo (explícito o inferido por [ApiController]) o por la query, y que no mira la ruta.
    [Fact]
    public void Detection_finds_body_query_and_form_parameters_including_the_inferred_body()
    {
        var inputs = ActionInputs(typeof(ProbeController))
            .Where(input => !IsAllowedInput(input.Parameter.ParameterType))
            .Select(input => (input.Parameter.Name, input.Source))
            .ToArray();

        Assert.Equal(
            [
                ("explicitBody", BindingSource.Body),
                ("inferredBody", BindingSource.Body),
                ("query", BindingSource.Query),
                ("form", BindingSource.Form),
            ],
            inputs);
    }

    [Theory]
    [InlineData(typeof(UpdateUserHttpRequest), true)]
    [InlineData(typeof(ExternalLoginQuery), true)]
    [InlineData(typeof(string), true)]
    [InlineData(typeof(int?), true)]
    [InlineData(typeof(bool?), true)]
    [InlineData(typeof(Guid), true)]
    [InlineData(typeof(UserInvitationChannel?), true)]
    [InlineData(typeof(CancellationToken), true)]
    [InlineData(typeof(UpdateUserRequest), false)]
    [InlineData(typeof(PhoneNumberInput), false)]
    [InlineData(typeof(Dictionary<string, string>), false)]
    public void Only_contracts_and_simple_values_are_allowed_as_input(Type type, bool expected) =>
        Assert.Equal(expected, IsAllowedInput(type));

    [Fact]
    public void Contracts_with_private_data_override_to_string()
    {
        // MVC registra los argumentos de una acción con su ToString(), y el que el compilador escribe para un record lista
        // todas las propiedades. Vale para los contratos del núcleo y para los de un módulo, en Modules/<M>/Contracts.
        var contracts = ApiAssembly.GetTypes().Where(type => type.ResidesIn(ContractsNamespace)).ToArray();

        // Si la búsqueda no encontrara contratos con datos privados, la regla pasaría en silencio.
        Assert.Contains(contracts, CarriesPrivateData);

        var leaking = Leaking(contracts).Select(type => type.FullName).ToArray();

        Assert.True(leaking.Length == 0, "Override ToString() in contracts with private data: " + string.Join(", ", leaking));
    }

    [Fact]
    public void Detection_flags_private_data_without_its_own_to_string()
    {
        // Casos de control, al pie de esta clase: el ToString() que el compilador escribe para un record no cuenta, y
        // Number cuenta solo a secas.
        Assert.Equal(
            [typeof(LeakingHttpRequest), typeof(PhoneHttpRequest)],
            Leaking([typeof(LeakingHttpRequest), typeof(PhoneHttpRequest), typeof(RedactedHttpRequest), typeof(VersionHttpRequest)]));
    }

    private static bool IsController(Type type) =>
        type is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(type);

    private static IEnumerable<ActionInput> ActionInputs(Type controller)
    {
        var actions = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && !method.IsDefined(typeof(NonActionAttribute), inherit: true));

        foreach (var action in actions)
        {
            foreach (var parameter in action.GetParameters())
            {
                if (InputSource(parameter) is { } source)
                {
                    yield return new ActionInput($"{controller.FullName}.{action.Name}", parameter, source);
                }
            }
        }
    }

    // Body, Query o Form si el parámetro lo declara; sin atributo, [ApiController] infiere el cuerpo para un tipo complejo.
    // Ruta, header o servicios no cuentan: no son contratos de entrada de contenido.
    private static BindingSource? InputSource(ParameterInfo parameter)
    {
        var declared = parameter.GetCustomAttributes(inherit: true)
            .OfType<IBindingSourceMetadata>()
            .Select(metadata => metadata.BindingSource)
            .FirstOrDefault(source => source is not null);

        if (declared is not null)
        {
            return declared == BindingSource.Body || declared == BindingSource.Query || declared == BindingSource.Form ? declared : null;
        }

        var type = parameter.ParameterType;
        return type == typeof(CancellationToken) || type.IsInterface || IsSimpleValue(type) ? null : BindingSource.Body;
    }

    private static bool IsAllowedInput(Type type) =>
        type == typeof(CancellationToken)
        || IsSimpleValue(type)
        || (type.Assembly == ApiAssembly && type.ResidesIn(ContractsNamespace));

    private static bool IsSimpleValue(Type type)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;
        return value.IsPrimitive || value.IsEnum || SimpleValueTypes.Contains(value);
    }

    /// <summary>Los contratos con datos privados que no escriben su propio ToString().</summary>
    private static Type[] Leaking(IEnumerable<Type> types) =>
        [.. types.Where(type => CarriesPrivateData(type) && !OverridesToString(type))];

    private static bool CarriesPrivateData(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(property => property.Name == PhoneNumber
            || PrivateDataNames.Any(name => property.Name.EndsWith(name, StringComparison.Ordinal)));

    // El de object no cuenta, ni el que el compilador escribe para un record: ese es el que lista el correo.
    private static bool OverridesToString(Type type) =>
        type.GetMethod(nameof(object.ToString), Type.EmptyTypes) is { } method
        && method.DeclaringType != typeof(object)
        && !method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private sealed record ActionInput(string Action, ParameterInfo Parameter, BindingSource Source);

    [ApiController]
    [Route("probe")]
    private sealed class ProbeController : ControllerBase
    {
        [HttpPost("{id:guid}")]
        public OkObjectResult Explicit([FromRoute] Guid id, [FromBody] UpdateUserRequest explicitBody) =>
            Ok(new { id, explicitBody });

        [HttpPut]
        public OkObjectResult Inferred(UpdateUserRequest inferredBody, CancellationToken cancellationToken) =>
            Ok(new { inferredBody, cancellationToken });

        [HttpGet("{phone}")]
        public OkObjectResult Query(
            [FromQuery] PhoneNumberInput query,
            [FromQuery] int? page,
            [FromRoute] PhoneNumberInput phone,
            [FromHeader] string? header) =>
            Ok(new { query, page, phone, header });
        [HttpPost("form")]
        public OkObjectResult Form([FromForm] UpdateUserRequest form) => Ok(form);
    }

    // Los casos de control de Detection_flags_private_data_without_its_own_to_string.
    private sealed record LeakingHttpRequest(string? Email, string? Name);

    private sealed record PhoneHttpRequest(string? Country, string? Number);

    private sealed record RedactedHttpRequest(string? CheckToken)
    {
        public override string ToString() => nameof(RedactedHttpRequest);
    }

    private sealed record VersionHttpRequest(long VersionNumber, string? Name);
}
