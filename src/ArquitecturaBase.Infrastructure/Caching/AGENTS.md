# Caching: Redis compartido

`RedisCache` es el único adaptador de datos cacheados; `RedisCacheKey`, scripts y opciones definen claves, leases y límites. `CachingRegistration` registra cliente, salud y métricas; `SystemSettingsCache` es el adaptador del área; un módulo tiene el suyo en su carpeta (ARCA: `Registry/ArcaRegistryCache`).

## Al modificar

- Las leases exclusivas también coordinan envíos externos; acquire, renew y release quedan en RedisCache. Renovación/liberación comparan propietario; release puede dejar un cooldown. No almacenan cuerpos ni números. Probar hosts independientes sobre Redis real.

- No agregar caché local, HybridCache ni otro acceso a Redis para datos. Un desarrollo usa el puerto de su área y este adaptador.
- Una fábrica corre en su propio scope y proyecta datos confirmados; no captura el contexto, entidades ni servicios scoped del request.
- Una fábrica que puede devolver la falla de un proveedor externo pasa `keep`: lo que rechaza se devuelve sin guardar, como un resultado grande, y el próximo fallo vuelve a leer.
- Definí clave, versión de formato, TTL, payload e invalidación después del commit, incluido seed. Los permisos no se cachean ([ADR 0032](../../../docs/decisions/0032-sin-redis-la-app-sigue.md)).
- Conservá los scripts de propietario del lease: invalidar impide que una fábrica anterior republique un valor viejo. No reemplazar el descarte por búsquedas globales de claves.
- Una caída deja readiness en Degraded (`CachingRegistration`) y la app sigue: quien puede seguir sin el caché atrapa `RedisCache.IsUnavailable` y lee la base, como los ajustes y el seed; el resto falla. Toda operación pública convierte en `TimeoutException` una cancelación que no pidió quien llama: con Redis colgado, el cliente cancela los comandos pendientes al soltar el socket. Probalo también con Redis pausado (`RedisFailureTests`). El descarte fallido después del commit no revierte PostgreSQL; probá fallas y concurrencia con Redis real y dos hosts.

## Referencias

- [Receta para nuevos usos](../../../docs/guides/agregar-cache.md).
- [Decisión de caché](../../../docs/decisions/0009-redis-como-unico-cache-de-datos.md).
- [Pruebas de Redis](../../../tests/ArquitecturaBase.Api.IntegrationTests/Caching).
