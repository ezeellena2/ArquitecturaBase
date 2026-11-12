using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
/// <summary>
/// Which choice question tells the currency of each number question of a form (DOC7-A, «Total de Monto (pesos)»), by the number's
/// field. The form's standard declares it with its reading (DOC1, the «Comprobante de transferencia»), and no standard declares it
/// yet: until one does, no number has a currency and the dashboard adds up each number question as a whole.
/// </summary>
internal static class FormDashboardCurrencyPolicy
{
    private static readonly IReadOnlyDictionary<Guid, Guid> None = new Dictionary<Guid, Guid>();

    public static IReadOnlyDictionary<Guid, Guid> Find(FormResponseDefinitionRow definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return None;
    }
}
