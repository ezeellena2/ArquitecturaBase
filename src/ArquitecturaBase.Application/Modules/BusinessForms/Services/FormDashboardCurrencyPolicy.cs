using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
/// <summary>
/// Which choice question tells the currency of each number question of a form (DOC7-A, «Total de Monto (pesos)»), by the number's
/// field. The form's standard declares it with its reading (DOC1: in the «Comprobante de transferencia», the amount goes with the
/// currency), and the copy maps both data to its questions through its standard key and version and its sealed bound map. Without a
/// standard, without a reading or with a datum the copy does not bind, the number has no currency and the dashboard adds it up whole.
/// </summary>
internal static class FormDashboardCurrencyPolicy
{
    private static readonly IReadOnlyDictionary<Guid, Guid> None = new Dictionary<Guid, Guid>();

    public static IReadOnlyDictionary<Guid, Guid> Find(FormResponseDefinitionRow definition, IEnumerable<StructuredFormStandard> standards)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(standards);
        if (definition.StandardKey is not { } key || definition.StandardBindingsJson is not { } bindingsJson) return None;
        if (standards.FirstOrDefault(standard => standard.Key == key && standard.Version == definition.StandardVersion)?.Reading is not { } reading) return None;
        var bound = FormDraftPolicy.Read<Dictionary<string, Guid>>(bindingsJson);
        return reading.Currencies.Where(pair => bound.ContainsKey(pair.Key) && bound.ContainsKey(pair.Value))
            .ToDictionary(pair => bound[pair.Key], pair => bound[pair.Value]);
    }
}
