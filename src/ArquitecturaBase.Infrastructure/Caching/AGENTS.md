# Caching: Redis compartido

`RedisCache` es el único adaptador de datos cacheados; `RedisCacheKey`, scripts y opciones definen claves, leases y límites. `CachingRegistration` registra cliente, salud y métricas; `SystemSettingsCache` es el adaptador del área.

## Al modificar

- No agregar caché local, HybridCache ni otro acceso a Redis para datos. Un desarrollo usa el puerto de su área y este adaptador.
- Una fábrica corre en su propio scope y proyecta datos confirmados; no captura el contexto, entidades ni servicios scoped del request.
- Definí clave, versión de formato, TTL, payload e invalidación después del commit, incluido seed. Para autorización, la clave incorpora una revisión confirmada del rol.
- Conservá los scripts de propietario del lease: invalidar impide que una fábrica anterior republique un valor viejo. No reemplazar el descarte por búsquedas globales de claves.
- Una caída falla readiness y operaciones dependientes. El descarte fallido después del commit no revierte PostgreSQL; probá fallas y concurrencia con Redis real y dos hosts.

## Referencias

- [Receta para nuevos usos](../../../docs/guides/agregar-cache.md).
- [Decisión de caché](../../../docs/decisions/0009-redis-como-unico-cache-de-datos.md).
- [Pruebas de Redis](../../../tests/ArquitecturaBase.Api.IntegrationTests/Caching).
