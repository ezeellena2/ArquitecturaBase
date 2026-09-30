# 0009: Redis como único caché de datos

## Estado

Aceptada, 2026-09-30. El usuario pidió Redis directo como base de los desarrollos futuros y autorizó su implementación.

## Contexto

Los permisos por rol y el modo de registro usaban `HybridCache` sin proveedor distribuido. Cada proceso conservaba datos e invalidaciones propios. La plantilla debe poder crecer con varias réplicas que compartan el mismo caché.

## Decisión

Usar Redis directamente con `StackExchange.Redis`, registrado por Aspire, como único caché de datos compartidos. Infrastructure encapsula el cliente en `RedisCache`; Application conserva sus puertos y AppHost levanta Redis junto con la Api.

## Consecuencias

- Se retiran HybridCache y su nivel local. Las fábricas leen en scopes independientes y solo el adaptador escribe en Redis; lo verifica `CacheBoundaryTests`.
- Cada lectura cacheada define clave, TTL, proyección e invalidaciones después del commit. Las leases con vencimiento y token coordinan misses entre réplicas y evitan que un dueño obsoleto publique después de una invalidación.
- Los permisos usan el `ConcurrencyStamp` confirmado del rol como revisión. Una revocación no depende de que una eliminación remota tenga éxito. Las revisiones anteriores expiran en una hora; los ajustes, en un minuto.
- Redis es una dependencia requerida: su caída falla readiness y las operaciones que lo usan. Un fallo al invalidar después del commit devuelve 500 genérico, aunque PostgreSQL ya haya confirmado. No se reintenta la transacción entera.
- El seed invalida después de confirmar; Redis puede sobrevivir al proceso. Producción provisiona Redis, conexión privada/TLS y un prefijo compartido entre réplicas y distinto entre instalaciones.
- No cambia el almacenamiento de tokens, códigos, Data Protection, las colas ni el rate limiter: requieren decisiones propias.

El detalle y la receta para nuevas áreas están en [backend.md](../architecture/backend.md#caché-compartido-en-redis) y [agregar-cache.md](../guides/agregar-cache.md).

## Alternativas descartadas

- **HybridCache con Redis de segundo nivel:** conserva datos locales e invalidaciones por proceso que el usuario pidió retirar.
- **IDistributedCache como adaptación:** no aporta las operaciones atómicas de coordinación y publicación necesarias; se conserva un único adaptador directo.
- **Un cliente Redis por área:** duplica conexiones y políticas, y permite cachear datos de una transacción aún no confirmada.
