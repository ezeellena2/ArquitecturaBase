# Extensions: consultas y locks de PostgreSQL

Reúne paginado y orden, patrones de búsqueda, filtros de modelo y locks transaccionales. Son mecanismos técnicos que usan repositorios y lectores.

## Al modificar

- Una extensión se queda en Infrastructure; ningún `IQueryable` sale por un contrato de Application.
- El orden paginado tiene desempate único. Reutilizá patrones de búsqueda para escapar comodines en vez de concatenar entrada.
- Las claves de advisory lock usan prefijos propios y orden determinista. Un prefijo nuevo se incorpora a sus pruebas y al inventario de límites.
- Los locks requieren el límite abierto y van antes de lecturas dependientes; no convertirlos en locks de proceso.

## Referencias

- [Locks y consultas](../../../../docs/architecture/backend.md).
- [Unicidad y prefijos de locks](../../../../docs/guides/agregar-un-area.md).
- [Pruebas de claves](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Persistence/AdvisoryLockKeysTests.cs).
