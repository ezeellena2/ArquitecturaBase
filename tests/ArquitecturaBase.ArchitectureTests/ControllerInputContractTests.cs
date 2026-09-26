using System.Reflection;
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

    [Fact]
    public void Controller_body_and_query_parameters_are_http_contracts()
    {
        // La entrada de una acción es un contrato de Api/Contracts, mapeado a mano al modelo de Application: así el
        // JSON que acepta la Api no cambia sin querer al tocar un modelo, y lo que MVC registra de los argumentos
        // (su ToString()) lo decide la Api, no Application.
        var controllers = ApiAssembly.GetTypes().Where(IsController).ToArray();
        var inputs = controllers.SelectMany(ActionInputs).ToArray();

        // Si la búsqueda dejara de encontrar los parámetros, la regla de abajo pasaría en silencio.
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
    public void Detection_finds_body_and_query_parameters_including_the_inferred_body()
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

    // Body o Query si el parámetro lo declara; sin atributo, [ApiController] infiere el cuerpo para un tipo complejo.
    // Ruta, header, form o servicios no cuentan: no son el cuerpo ni la query de la petición.
    private static BindingSource? InputSource(ParameterInfo parameter)
    {
        var declared = parameter.GetCustomAttributes(inherit: true)
            .OfType<IBindingSourceMetadata>()
            .Select(metadata => metadata.BindingSource)
            .FirstOrDefault(source => source is not null);

        if (declared is not null)
        {
            return declared == BindingSource.Body || declared == BindingSource.Query ? declared : null;
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
    }
}
