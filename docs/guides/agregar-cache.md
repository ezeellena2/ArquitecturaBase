# Agregar una lectura cacheada

Antes de modificar un consumidor, leé las instrucciones de su área y de [Infrastructure/Caching](../../src/ArquitecturaBase.Infrastructure/Caching/AGENTS.md). Si agregás un adaptador de área, documentá clave, revisión, TTL, descarte y prueba de concurrencia con la [receta de instrucciones por carpeta](instrucciones-por-carpeta.md).

Redis es el único caché de datos compartidos ([ADR 0009](../decisions/0009-redis-como-unico-cache-de-datos.md)). Usá este patrón para cada área nueva, también dentro de un módulo opcional. Primero justificá la lectura que conviene cachear: el uso de Redis no obliga a cachear toda entidad o listado.

1. **Definí proyección, clave y TTL.** Usá DTOs o valores serializables con `System.Text.Json`, nunca entidades seguidas ni `IQueryable`. La clave lógica `RedisCacheKey` vive en Infrastructure e incluye todos los filtros, tenant y cultura que cambian el resultado; no lleva secretos ni `{`/`}`. El adaptador agrega prefijo, `v1` del formato y un hash tag común a datos y lease, compatible con Redis Cluster. Cambiar `v1` separa formatos; no reemplaza la revisión del dato.
2. **Leé desde un scope nuevo.** El lector usa `RedisCache.GetOrCreateInOwnScopeAsync<TReader,TState,TValue>`. La fábrica resuelve su dependencia en el scope del adaptador; el estado lleva identificadores y filtros, nunca contextos, entidades seguidas ni servicios del llamador. Debe respetar el token. No hay un `Set` público ni se escribe con `IConnectionMultiplexer` desde el área. Si la fábrica puede devolver la falla de un proveedor externo, pasá `keep`: lo que no acepta se devuelve sin guardar y el próximo fallo vuelve a leer, como en `ArcaRegistryCache`.
3. **Definí el descarte.** Un servicio que escribe consume un puerto del área en `Application/Interfaces/Integrations/Caching`, como `ISystemSettingsCache`. Su adaptador delega en `RedisCache.RemoveAsync`. El servicio lo llama después de `ExecuteInTransactionAsync` y solo con un resultado confirmado. El lector no invalida.
4. **Elegí la coherencia.** El descarte elimina dato y lease atómicamente: una fábrica anterior puede devolver su lectura vieja, pero no publicarla después del descarte. Los datos de seguridad no se cachean: los permisos se leen de la base en cada pedido ([ADR 0032](../decisions/0032-sin-redis-la-app-sigue.md)). Si un dato cacheado no puede atrasarse, la clave lleva una revisión confirmada, para que una eliminación fallida después del commit no resucite lo viejo. Para datos que admiten atraso, documentá el TTL máximo durante una caída. Si el dato cambia por seed, invalidá también después de ese commit.
5. **Probalo con Redis real.** Usá dos hosts con conexiones independientes que compartan base y prefijo. Cubrí hit, TTL, descarte desde otra instancia, múltiples misses, rollback y fábrica tardía. Para revisiones, tanto agregado como revocación. Decidí también qué hace la lectura con Redis caído: si la operación puede seguir sin él, leé la base sin cachear cuando `RedisCache.IsUnavailable` reconoce la excepción, como `SystemSettingsReader`, y probalo con Redis apagado (`RedisFailureTests`). `CacheBoundaryTests` impide proveedores locales y escrituras directas fuera del adaptador; las invalidaciones funcionales las prueba tu área.
6. **Registrá y documentá.** El adaptador técnico se registra en el DI dueño; los repositorios y lectores, solo en `PersistenceRegistration`. Application y Domain no agregan Redis. Documentá claves, TTL e invalidaciones en las reglas del área. Corré build sin advertencias y todos los tests con Docker.

## Ejemplo existente

`SystemSettingsReader` lee un enum durante 60 segundos sin capturar el contexto del request:

```csharp
cache.GetOrCreateInOwnScopeAsync<ApplicationDbContext, int, RegistrationMode>(
    SystemSettingsCache.CacheKey, 0,
    static (db, _, ct) => db.SystemSettings.AsNoTracking()
        .Select(settings => settings.RegistrationMode).FirstOrDefaultAsync(ct),
    TimeSpan.FromSeconds(60), cancellationToken);
```

`SystemSettingsService.UpdateAsync` confirma primero y después llama a `ISystemSettingsCache.InvalidateAsync`. El contexto directo del ejemplo es la lectura técnica de una configuración singleton; las consultas de negocio de otra área siguen detrás de sus lectores especializados.

## Configuración y fallas

AppHost entrega `ConnectionStrings:cache`. Fuera de Aspire configurá `ConnectionStrings__cache` como secreto y provisioná Redis por separado, con TLS y red privada en producción.

`Caching:KeyPrefix` coincide entre réplicas y difiere entre instalaciones y ambientes. Fuera de Development y Testing es obligatorio: sin él la Api no arranca. En esos dos se deriva de aplicación, ambiente y nombre de la base, y las pruebas derivan el suyo de la base de prueba.

Opciones validadas: `MaximumPayloadBytes` (1 MiB por defecto, máximo 16 MiB), `LeaseSeconds` (15), `WaitSeconds` (30) y `RetryMilliseconds` (25). Conexión y comandos tienen timeout de 3 segundos. Un resultado grande se devuelve sin guardar. Un dueño que pierde su lease no publica ni libera la de otro; las leases de llenado de caché no se renuevan. Una fábrica lenta puede repetirse después de 15 segundos: debe ser de solo lectura. La espera total se cancela a los 30 segundos y la fábrica debe respetar esa cancelación.

El mismo adaptador ofrece leases exclusivas para coordinación técnica entre hosts. No almacenan
una proyección ni usan los plazos de llenado anteriores: el consumidor define duración, renovación,
cancelación al perder propiedad y cooldown. WhatsApp usa una clave por hash del destinatario,
60 segundos de TTL, renovación cada diez y seis de cooldown para ambos transports. Su contrato
está en [WhatsApp](../features/whatsapp.md#chat-de-atención--2026-10-02); no reemplaza los locks ni
la persistencia de PostgreSQL.

Durante una caída la app sigue (N9, [ADR 0032](../decisions/0032-sin-redis-la-app-sigue.md)): el chequeo de Redis deja `/health` en Degraded, que responde 200, y `/alive` continúa sano. Los ajustes se leen de la base sin cachear; una lectura sin ese respaldo (la consulta de ARCA, la lease del envío de WhatsApp) falla. Si la invalidación falla después del commit, responde 500 con `traceId`, pero la escritura ya ocurrió. No reintentes todo el caso de uso suponiendo un rollback. Los ajustes pueden conservar el valor viejo hasta su TTL. El seed no frena el arranque si no puede descartar los ajustes.

Aspire agrega trazas del cliente y el meter `ArquitecturaBase.Caching`: `cache.hits`, `cache.misses`, `cache.factory.duration`, `cache.lease.wait.duration` y `cache.payload.size`. No llevan claves ni datos personales como etiquetas. Un miss que espera a otra instancia puede terminar como hit: los contadores describen ambas etapas.
