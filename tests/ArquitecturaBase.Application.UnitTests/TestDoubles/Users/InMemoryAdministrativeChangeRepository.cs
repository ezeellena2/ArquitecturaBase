using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Users;

/// <summary>
/// La historia de los cambios administrativos en memoria. Como el repositorio real, sólo agrega: lanza fuera del límite,
/// así un test ve que la fila se escribe adentro del de su cambio.
/// </summary>
internal sealed class InMemoryAdministrativeChangeRepository : IAdministrativeChangeRepository
{
    private readonly List<AdministrativeChange> _changes = [];

    public Func<bool>? InTransaction { get; set; }

    public IReadOnlyList<AdministrativeChange> Changes => _changes;

    public void Add(AdministrativeChange change)
    {
        TransactionGuard.Require(InTransaction);
        _changes.Add(change);
    }
}
