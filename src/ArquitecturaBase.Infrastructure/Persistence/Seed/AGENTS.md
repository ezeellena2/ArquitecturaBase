# Seed: datos iniciales e idempotencia

`DatabaseSeeder` coordina roles, ajustes y cliente OpenIddict en una sola transacción. Los seeders parciales describen su parte y no abren límites propios.

## Al modificar

- El lock de seed serializa réplicas antes de leer. Conservá un único commit y rollback completo si un seeder falla.
- El seed debe ser idempotente: los ajustes existentes no se reemplazan por defaults de configuración.
- Un permiso nuevo se incorpora al catálogo y al rol Admin; conservá roles y cliente del sistema.
- Después del commit completo se invalidan permisos y ajustes en Redis. No descartar desde un seeder parcial ni omitirlo porque sea arranque.
- Probá arranque repetido, dos hosts y un Redis que conserva entradas entre procesos.

## Referencias

- [Excepción técnica del límite y caché](../../../../docs/architecture/backend.md).
- [Actualización del seed de permisos](../../../../docs/guides/permiso-nuevo.md).
- [Invalidación después del seed](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Caching/RedisSeedTests.cs).

IDatabaseSeedParticipant permite aportes técnicos de módulos opcionales dentro del mismo lock/límite de DatabaseSeeder. Los participantes se registran en su módulo con TryAddEnumerable; el núcleo no nombra implementaciones opcionales. No abren transacciones, guardan ni ejecutan efectos externos.
