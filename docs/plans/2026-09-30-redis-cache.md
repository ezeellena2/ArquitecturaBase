# Implementación de Redis como único caché de datos del backend

**Estado:** implementado y verificado el 2026-09-30. Decisión: [ADR 0009](../decisions/0009-redis-como-unico-cache-de-datos.md).

## Resultado

Redis reemplaza el HybridCache en memoria de permisos por rol y modo de registro. AppHost levanta `cache` con `redis:8.6`, la misma imagen que el arnés de integración y la versión del recurso de Aspire 13.5.4. La Api lo referencia y espera. Infrastructure registra un cliente singleton mediante Aspire y un único adaptador de caché; Application y Domain no tienen dependencias Redis.

PostgreSQL sigue siendo la fuente. No hay un nivel local ni fallback durante una caída de Redis. La memoización de plantillas embebidas, las colas, el rate limiter y Data Protection conservan sus reglas: este cambio reemplaza el caché de datos compartidos.

## Archivos finales

`+` creado, `~` modificado, `-` retirado. El árbol contiene solo archivos de esta implementación; los cambios del otro chat en ingreso y registro quedan fuera de su alcance.

```text
ArquitecturaBase/
├─ .github/workflows/deploy.yml                                  ~ conexiones de EF
├─ AGENTS.md                                                     ~
├─ README.md                                                     ~
├─ Directory.Packages.props                                      ~
├─ src/
│  ├─ ArquitecturaBase.AppHost/
│  │  ├─ ArquitecturaBase.AppHost.csproj                           ~
│  │  └─ AppHost.cs                                              ~
│  ├─ ArquitecturaBase.Api/Program.cs                             ~
│  ├─ ArquitecturaBase.Application/
│  │  ├─ Interfaces/Persistence/IPermissionReader.cs              ~
│  │  ├─ Interfaces/Persistence/ISystemSettingsReader.cs          ~ comentario
│  │  └─ Models/Identity/RolePermissionsRow.cs                     +
│  └─ ArquitecturaBase.Infrastructure/
│     ├─ ArquitecturaBase.Infrastructure.csproj                    ~
│     ├─ DependencyInjection.cs                                   ~
│     ├─ Caching/
│     │  ├─ CachingRegistration.cs                                 +
│     │  ├─ RedisCache.cs                                          +
│     │  ├─ RedisCacheOptions.cs                                   +
│     │  ├─ RedisCacheKey.cs                                       +
│     │  ├─ RedisCacheScripts.cs                                   +
│     │  ├─ SystemSettingsCache.cs                                 ~
│     │  └─ HybridCacheExtensions.cs                               -
│     ├─ Identity/
│     │  ├─ IdentityRegistration.cs                                ~
│     │  └─ PermissionService.cs                                   ~
│     └─ Persistence/
│        ├─ Readers/
│        │  ├─ PermissionReader.cs                                 ~
│        │  └─ SystemSettingsReader.cs                             ~
│        └─ Seed/
│           ├─ DatabaseSeeder.cs                                  ~
│           └─ RoleSeeder.cs                                      ~
├─ tests/
│  ├─ ArquitecturaBase.Api.IntegrationTests/
│  │  ├─ ArquitecturaBase.Api.IntegrationTests.csproj              ~
│  │  ├─ Support/
│  │  │  ├─ ApiFactory.cs                                         ~
│  │  │  └─ CacheTestHost.cs                                      +
│  │  ├─ TestFeatures/Caching/
│  │  │  ├─ ControlledCacheReader.cs                              +
│  │  │  └─ FailingSystemSettingsCache.cs                         +
│  │  ├─ Caching/
│  │  │  ├─ RedisCacheTests.cs                                    +
│  │  │  ├─ RedisCacheConcurrencyTests.cs                         +
│  │  │  ├─ RedisFailureTests.cs                                  +
│  │  │  └─ RedisSeedTests.cs                                     +
│  │  ├─ Identity/PermissionServiceTests.cs                        ~
│  │  ├─ Settings/SystemSettingsReaderTests.cs                    ~ comentario adaptado
│  │  └─ HealthCheckTests.cs                                      ~
│  └─ ArquitecturaBase.ArchitectureTests/
│     ├─ CacheBoundaryTests.cs                                    +
│     └─ TransactionBoundaryTests.cs                              ~
└─ docs/
   ├─ architecture/backend.md                                    ~
   ├─ decisions/
   │  ├─ 0009-redis-como-unico-cache-de-datos.md                    +
   │  └─ README.md                                               ~
   ├─ features/
   │  ├─ identidad.md                                            ~ párrafo de permisos
   │  └─ administracion.md                                       ~ párrafo de ajustes
   ├─ guides/
   │  ├─ agregar-cache.md                                        +
   │  ├─ despliegue.md                                           ~
   │  ├─ migracion.md                                            ~ conexiones de EF
   │  ├─ quitar-whatsapp.md                                      ~ conexiones de EF
   │  └─ permiso-nuevo.md                                        ~ revisión y seed
   └─ plans/2026-09-30-redis-cache.md                              ~
```

No fue necesaria una migración. Las pruebas comprobaron que el `ConcurrencyStamp` existente avanza al actualizar el rol y al agregar o sacar claims mediante Identity, también desde el seed. Los tests con bases propias se aíslan por el prefijo derivado de su nombre; no hizo falta editar `SystemSettingsSeedTests` ni `ProductionStartupTests`.

## Qué hace el código

| Pieza | Responsabilidad |
|---|---|
| `CachingRegistration` | Cliente directo de Aspire, conexión obligatoria, timeouts de 3 segundos y sin backlog durante desconexión; adaptador, opciones y métricas |
| `RedisCache` | JSON con envoltorio para distinguir null de miss; TTL; fábricas en scope propio; leases por clave; descarte y publicación condicionada al token |
| `RedisCacheKey` / `RedisCacheScripts` | Prefijo, versión del formato y hash tag común para datos y lease; scripts atómicos compatibles con slots de Cluster |
| `RedisCacheOptions` | Prefijo por instalación/ambiente, 1 MiB máximo por defecto, lease de 15 s, espera de 30 s y reintentos de 25 ms |
| `PermissionService` / `PermissionReader` | Roles de la cuenta frescos; revisión confirmada en scope propio; permisos de esa revisión en una sola consulta; hasta cuatro reintentos si cambió antes del llenado |
| `SystemSettingsReader` / `SystemSettingsCache` | Enum cacheado 60 s y descarte después del commit; el puerto de Application conserva su contrato |
| `DatabaseSeeder` / `RoleSeeder` | Roles afectados y descarte de permisos y ajustes después de confirmar el seed completo |
| `ApiFactory` / `CacheTestHost` | PostgreSQL y Redis reales; conexiones independientes y prefijos comunes entre réplicas o distintos entre bases |
| `CacheBoundaryTests` | Detectores de cliente/escrituras fuera del adaptador y registro, y prohibición de proveedores locales/HybridCache con controles que comprueban que el detector funciona |

Una lease vencida o eliminada impide que el dueño anterior publique o libere la del siguiente. La invalidación elimina dato y lease. Un request iniciado antes del cambio puede devolver lo que ya leyó; no puede volver a dejarlo como caché vigente después del descarte. Para permisos, una entrada de una revisión anterior nunca se selecciona desde una lectura de la revisión nueva, incluso si falló el descarte remoto.

Redis es una dependencia requerida: falla readiness y las operaciones que lo usan, pero no liveness. Una invalidación que falla después del commit puede responder 500 con la escritura ya guardada. En ajustes, el TTL de 60 segundos acota el valor viejo que sobreviva; en permisos, la revisión protege la revocación. No se reintenta una transacción confirmada.

## Anclaje para futuros desarrollos

Las reglas están en `AGENTS.md`, el contrato técnico en [backend.md](../architecture/backend.md#caché-compartido-en-redis), la decisión en el ADR y la receta en [agregar-cache.md](../guides/agregar-cache.md). La prueba de arquitectura evita variantes del proveedor; cada área prueba sus propias invalidaciones.

Una nueva área declara proyección, clave y TTL en Infrastructure. Su lector usa `GetOrCreateInOwnScopeAsync`, con fábrica estática que recibe identificadores/filtros y respeta el token. Si escribe datos cacheados, Application consume un puerto de invalidación del área; el adaptador lo implementa con `RemoveAsync`, y el servicio lo llama después del commit. Los controllers siguen inyectando servicios. Un módulo opcional sigue el patrón dentro de sus propias carpetas.

En producción fuera de Aspire se provisiona Redis y se configura `ConnectionStrings__cache` como secreto, TLS y red privada. `Caching__KeyPrefix` coincide entre réplicas y difiere entre instalaciones/ambientes; el valor derivado de aplicación, ambiente y base no distingue dos servidores con el mismo nombre de base.

## Verificación realizada

- TDD inicial: el test de registro falló sin Redis y el de revocación sin invalidación falló con los permisos anteriores. El test de seed falló porque devolvía el modo restrictivo precargado después de crear la fila en Open; los tres pasaron con sus cambios.
- Build completo sin advertencias ni errores. Se usó `--artifacts-path artifacts/redis` para evitar los DLL bloqueados por la Api que estaba abierta al empezar.
- `dotnet test --artifacts-path artifacts/redis`: **1984 tests correctos, 0 errores, 0 omitidos**, con Docker, PostgreSQL 18.3 y Redis 8.6.
- Hosts independientes: datos compartidos, prefijos aislados, null, TTL, tamaño máximo y disposición del scope.
- Barreras controladas: 32 misses ejecutan una sola fábrica, invalidación durante llenado, lease vencida que no publica ni borra la de otro, cancelación y espera acotada de un dueño muerto.
- Permisos: revisión ante agregado/revocación entre dos APIs, rollback sin cachear datos no confirmados, seed que repone un permiso y avanza la revisión.
- Caída real y recuperación de Redis sin reemplazar el cliente, readiness 503/liveness 200, error HTTP genérico con traceId y prueba de PostgreSQL confirmado cuando falla la invalidación posterior.
- Smoke de `aspire run --isolated`: PostgreSQL, Redis, Api y front saludables; 200 en health/alive/login-methods, JSON a través del proxy del front, reinicio de Api con el mismo Redis y nuevas lecturas correctas. Trazas GET/SET/EVAL/PING visibles mediante la telemetría de Aspire. Se terminó con `aspire stop` y la lista de AppHosts quedó vacía.
- Métricas emitidas: hits, misses, duración de fábricas, espera de lease y tamaño de valores. No se afirma un benchmark de capacidad ni una mejora cuantificada de rendimiento.
- EF: el comando de comprobación falló al faltar la conexión de Redis; pasó con ambas conexiones declaradas y confirmó que no falta una migración. Se generó un bundle y se aplicaron sus 12 migraciones sobre PostgreSQL vacío, con Redis apuntando a un puerto cerrado: la construcción declara la conexión, pero no necesita ese servidor. Se ajustaron generación y aplicación del bundle en el pipeline y las recetas. El contenedor temporal se eliminó al terminar.

Se trabajó en `main` con parches acotados, preservando los cambios del otro chat. No se crearon ramas ni se hizo push o un commit que mezclara esos cambios.

Fuentes usadas para la integración: [Redis en AppHost](https://aspire.dev/integrations/caching/redis-distributed/redis-distributed-host/) y [cliente Redis de Aspire](https://source.dot.net/Aspire.StackExchange.Redis/AspireRedisExtensions.cs.html), más las APIs y versiones de los paquetes restaurados en este proyecto.
