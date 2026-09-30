# Persistence: PostgreSQL y límite de guardado

`ApplicationDbContext` configura el modelo; `UnitOfWork` abre, confirma y deshace transacciones; `PersistenceRegistration` registra contexto, interceptores, repositorios, lectores, salud y seed.

## Al modificar

- Consultas de negocio en `Repositories` o `Readers`; el contexto directo queda para componentes técnicos. No filtrar `IQueryable` a otras capas.
- El único límite es `IUnitOfWork`; los repositorios, lectores, helpers y seeders parciales no guardan por su cuenta. `DatabaseSeeder` es la excepción técnica que coordina un único límite.
- Identity autoguarda sobre el contexto scoped dentro del límite; no agregar una estrategia que reejecute trabajo con efectos de envío.
- El soft delete se ejecuta antes de la auditoría. No usar operaciones bulk que salteen interceptores con entidades auditables o borrables.
- Los módulos registran su persistencia en su registro propio. Un cambio de esquema requiere configuración, migración y prueba con PostgreSQL.

## Referencias

- [Persistencia y transacciones](../../../docs/architecture/backend.md).
- [Migraciones](../../../docs/guides/migracion.md).
- [Pruebas de persistencia](../../../tests/ArquitecturaBase.Api.IntegrationTests/Persistence).
