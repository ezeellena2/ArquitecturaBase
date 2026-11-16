using System.Reflection;
using System.Runtime.CompilerServices;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.ArchitectureTests;

// Los modelos neutros que un módulo le pasa al núcleo, o el núcleo a un módulo, llevan respuestas de personas, textos de
// documentos y lo que dijo el modelo (N26). El ToString() que el compilador escribe para un record lista cada propiedad, así
// que un log que imprima uno de esos records las copia enteras: el record que tiene un valor escribe su propio ToString().
public sealed class NeutralModelRedactionTests
{
    private static readonly Assembly ApplicationAssembly = Assembly.Load("ArquitecturaBase.Application");

    private static readonly string[] Namespaces =
    [
        "ArquitecturaBase.Application.Models.StructuredData",
        "ArquitecturaBase.Application.Models.Assistant",
    ];

    // Los records con un valor que no lo redactan a propósito, cada uno con su motivo: sus textos son claves, códigos, estados,
    // huellas o lo que escribe quien administra el sistema, nunca lo que escribió una persona en una respuesta o un archivo, ni
    // lo que dijo un modelo. Un dato que deja de cumplirlo saca su record de esta lista.
    private static readonly Dictionary<Type, string> Exceptions = new()
    {
        [typeof(StructuredAssistantFormRow)] = "the form's name and when to offer it, written by whoever manages the form",
        [typeof(StructuredFeedCatalog)] = "the key and the value type of a catalog a module brings; its names go in dictionaries",
        [typeof(StructuredFeedItem)] = "the code and the value of an option of an external catalog, like a VAT rate",
        [typeof(StructuredFeedReadResponse)] = "a failure code",
        [typeof(StructuredFormIssueResponse)] = "the technical path of a check problem and its culture",
        [typeof(StructuredFormCultureResponse)] = "a culture and a compilation fingerprint",
        [typeof(StructuredFormCheckResponse)] = "the profile revision, the compatibility and the channel's technical evidence",
        [typeof(StructuredFormFileSettings)] = "where the photos come from",
        [typeof(StructuredFormPreparationIssue)] = "a question's label, a code and the name of the missing data, never its value",
        [typeof(StructuredFormPublicationCulture)] = "a culture and a compilation fingerprint",
        [typeof(StructuredFormStandardFolder)] = "the key of a folder of a standard; its names go in a dictionary",
        [typeof(StructuredRecordActionResponse)] = "a state and a failure code",
        [typeof(StructuredRecordActionChoice)] = "a button of an action, written by whoever manages the form",
        [typeof(StructuredRecordCaptureResponse)] = "a state and an error code",
        [typeof(StructuredRecordRepetition)] = "the number of the repeated response",
        [typeof(StructuredRecordReadReply)] = "the response's number, the form's name and its amendment; the answers go in a list, which is not printed",
        [typeof(AssistantCapabilityReference)] = "a capability's id and version",
        [typeof(AssistantTerminalReference)] = "a terminal's id and version",
        [typeof(AssistantCapabilityDefinition)] = "the description and schemas the capability's owner writes in its code",
        [typeof(AssistantAvailabilityResponse)] = "a state",
        [typeof(AssistantOperationAcceptedResponse)] = "a state",
        [typeof(AssistantOperationProvenance)] = "the ids of the model profile, the instructions and the channel; never a prompt",
    };

    [Fact]
    public void Neutral_records_with_a_value_override_to_string()
    {
        var records = ApplicationAssembly.GetTypes()
            .Where(type => Namespaces.Any(type.ResidesIn) && IsRecord(type))
            .ToArray();

        // Si la búsqueda dejara de encontrar records con valores, la regla pasaría en silencio.
        Assert.Contains(records, CarriesValues);

        var leaking = Leaking(records)
            .Where(type => !Exceptions.ContainsKey(type))
            .Select(type => type.FullName)
            .ToArray();

        Assert.True(leaking.Length == 0, "Override ToString() in neutral records with a value: " + string.Join(", ", leaking));
    }

    [Fact]
    public void Every_exception_is_a_neutral_record_with_a_value_that_does_not_override_to_string()
    {
        // Una excepción que ya redacta, que no tiene valores o que se mudó de lugar sobra: se borra en lugar de quedar como
        // permiso para el futuro.
        var stale = Exceptions.Keys
            .Where(type => !Namespaces.Any(type.ResidesIn) || !IsRecord(type) || Leaking([type]).Length == 0)
            .Select(type => type.FullName)
            .ToArray();

        Assert.True(stale.Length == 0, "Remove the stale exceptions: " + string.Join(", ", stale));
    }

    [Fact]
    public void Detection_flags_a_value_without_its_own_to_string()
    {
        // Casos de control, al pie de esta clase: el ToString() que el compilador escribe no cuenta, un record con sólo ids no
        // tiene valores, y un record anidado que filtra hace filtrar al que lo lleva.
        Assert.Equal(
            [typeof(TextRecord), typeof(OpenValueRecord), typeof(NestedLeakRecord), typeof(TextRecordStruct)],
            Leaking([
                typeof(TextRecord), typeof(OpenValueRecord), typeof(NestedLeakRecord), typeof(TextRecordStruct),
                typeof(RedactedRecord), typeof(IdsRecord), typeof(NestedRedactedRecord), typeof(ListRecord),
            ]));
    }

    /// <summary>Los records con un valor que no escriben su propio ToString().</summary>
    private static Type[] Leaking(IEnumerable<Type> types) =>
        [.. types.Where(type => CarriesValues(type) && !OverridesToString(type))];

    // Lo que el ToString() del compilador escribe entero: un texto, un valor abierto o un record anidado que a su vez lo
    // escribe. Una lista o un diccionario no: imprime el nombre de su tipo, no lo que tiene.
    private static bool CarriesValues(Type type) =>
        PrintedMemberTypes(type).Any(member => IsValue(member, [type]));

    private static bool IsValue(Type type, HashSet<Type> visiting)
    {
        var value = Nullable.GetUnderlyingType(type) ?? type;
        if (value == typeof(string) || value == typeof(object) || value == typeof(char))
        {
            return true;
        }

        return IsRecord(value) && !OverridesToString(value) && visiting.Add(value)
            && PrintedMemberTypes(value).Any(member => IsValue(member, visiting));
    }

    // Las propiedades y los campos públicos de instancia: lo que lista PrintMembers.
    private static IEnumerable<Type> PrintedMemberTypes(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0 && property.Name != "EqualityContract")
            .Select(property => property.PropertyType)
            .Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(field => field.FieldType));

    // Un record, de clase o de struct: el compilador le escribe PrintMembers.
    private static bool IsRecord(Type type) =>
        type.GetMethod("PrintMembers", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(System.Text.StringBuilder)]) is { } method
        && method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    // El de object o ValueType no cuenta, ni el que el compilador escribe para un record: ese es el que lista los valores.
    private static bool OverridesToString(Type type) =>
        type.GetMethod(nameof(object.ToString), Type.EmptyTypes) is { } method
        && method.DeclaringType != typeof(object)
        && method.DeclaringType != typeof(ValueType)
        && !method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    // Los casos de control de Detection_flags_a_value_without_its_own_to_string.
    private sealed record TextRecord(Guid Id, string Text);

    private sealed record OpenValueRecord(Guid Id, object? Value);

    private sealed record NestedLeakRecord(Guid Id, TextRecord Inner);

    private readonly record struct TextRecordStruct(string Text);

    private sealed record RedactedRecord(string Text)
    {
        public override string ToString() => nameof(RedactedRecord);
    }

    private sealed record IdsRecord(Guid Id, long Revision, bool Done, DayOfWeek Day, DateTimeOffset AtUtc);

    private sealed record NestedRedactedRecord(Guid Id, RedactedRecord Inner);

    private sealed record ListRecord(IReadOnlyList<string> Texts, IReadOnlyDictionary<string, string> Map);
}
