# Puertos de persistencia

Define repositorios, lectores y la unidad de trabajo. Separa entidades seguidas para modificar de proyecciones para consultar.

## Al modificar

- `Get` devuelve una entidad seguida desde un repositorio; `Find` devuelve una proyección; `List`, `Exists`, `Count` y `Lock` expresan su resultado. Un lector solo lee.
- Los contratos no filtran EF ni `IQueryable`. Las colecciones y filas se materializan en Infrastructure.
- `IUnitOfWork` es el límite explícito; `OnAnyResult` se usa cuando también hay que confirmar intentos, consumos o auditoría al fallar.
- Los locks se declaran donde se necesita exclusión real y se toman antes de las lecturas dentro de la transacción.

## Referencias

- [Persistencia y transacciones](../../../../docs/architecture/backend.md).
- [Convención de nombres](../../../../docs/decisions/0008-nombres-de-repositorios-y-lectores.md).
- [Lector de referencia](IRoleReader.cs).
