# Repositories: entidades seguidas y escritura

Implementa los puertos que cargan entidades para modificar, agregan o quitan filas y toman locks. La coordinación transaccional pertenece al servicio.

## Al modificar

- `Get` devuelve una entidad seguida; los `Lock` requieren una transacción activa y preceden a las lecturas. No usar `AsNoTracking`.
- No llamar `SaveChanges` ni abrir límites propios. Las operaciones de Identity conservan su guardado técnico dentro del mismo contexto y transacción.
- El borrado lógico usa el flujo de los interceptores. Un purgado físico técnico solo sigue la excepción documentada de su entidad.
- Una carrera se resuelve con índices o locks según la regla funcional; no ocultarla con una verificación que solo cubre el caso secuencial.

## Referencias

- [Nombres de repositorios y locks](../../../../docs/architecture/backend.md).
- [Repositorio de referencia](SystemSettingsRepository.cs).
- [Convención de persistencia](../../../../tests/ArquitecturaBase.ArchitectureTests/PersistenceNamingTests.cs).
