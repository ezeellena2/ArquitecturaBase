# Readers: proyecciones de consulta

Implementa consultas sin seguimiento, materializadas en filas o respuestas de Application. Encapsula filtros, conteos, búsqueda, orden y paginado.

## Al modificar

- Usá `AsNoTracking` y proyecciones; no devolver entidades para modificar ni `IQueryable`.
- Para listados usá `ApplySort` y `ToPagedResultAsync`, con whitelist y desempate único. La búsqueda usa los patrones comunes.
- Los conteos por filtro aplican los demás filtros e ignoran el propio; probá filtros combinados y cero resultados.
- Un lector cacheado usa el adaptador único y una fábrica en scope independiente; si puede seguir sin Redis, lee la base sin cachear cuando Redis no responde (`SystemSettingsReader`). Los permisos no se cachean: `PermissionReader` hace una sola consulta con el contexto del pedido.

## Referencias

- [Filtros y conteos](../../../../docs/features/administracion.md).
- [Lector de referencia](RoleReader.cs).
- [Lecturas con Redis](../../../../docs/guides/agregar-cache.md).
