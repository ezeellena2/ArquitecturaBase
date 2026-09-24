# ArquitecturaBase: reglas para agentes

La arquitectura canónica del backend está en [`docs/specs/2026-09-24-backend-mvc-architecture.md`](docs/specs/2026-09-24-backend-mvc-architecture.md). El plan para llevar el código existente a esa estructura está en [`docs/plans/2026-09-23-migracion-mvc-servicios-repositorios.md`](docs/plans/2026-09-23-migracion-mvc-servicios-repositorios.md). `CLAUDE.md` conserva las reglas operativas y funcionales del proyecto. Si una descripción antigua de carpetas o del pipeline de handlers contradice la arquitectura canónica, prevalece la especificación nueva.

## Estructura obligatoria para código backend nuevo

```text
Api/Controllers → Application/Interfaces/Services → Application/Services/<Área>
                → Application/Interfaces/{Persistence,Integrations}
                → Infrastructure/{Persistence/Repositories,Persistence/Readers,adaptadores}
                → ApplicationDbContext o proveedor técnico
```

- `Api` contiene controllers MVC, contratos HTTP y adaptación de protocolo. Un controller no consulta EF ni inyecta repositorios, lectores o handlers; depende de interfaces de servicios de `Application`.
- `Application` contiene servicios de casos de uso, sus interfaces, modelos, validadores y contratos de persistencia/integración. No usa `ApplicationDbContext`, EF Core, `HttpContext` ni tipos de `Infrastructure` o `Api`.
- `Domain` contiene el modelo y las reglas puras. Los contratos de repositorios/lectores que consumen los servicios van en `Application/Interfaces/Persistence`, no en `Domain`.
- `Infrastructure` implementa repositorios y lectores especializados, `ApplicationDbContext`, Identity, OpenIddict, correo, WhatsApp y workers técnicos. Las consultas EF de negocio quedan detrás de repositorios o lectores; se permite acceso directo al contexto para componentes técnicos de infraestructura como migraciones, seed, stores, interceptores y Unit of Work.
- Registrar dependencias en la capa dueña y componerlas desde `Program.cs`. Mantener `Result`, FluentValidation, `IUnitOfWork`, permisos, ProblemDetails y logging según las reglas funcionales vigentes. No crear un repositorio genérico por simetría.

## Transición sin regresiones

Los `Api/Endpoints`, `Application/Features`, handlers y decoradores actuales son código **pendiente de migración**, no una convención para nuevas funciones. No agregar nuevas rutas de negocio con Minimal API ni nuevos `IEndpoint`, `ICommandHandler` o `IQueryHandler`. Una corrección necesaria en código heredado puede hacerse allí hasta migrar su área; no exige convertir todo el sistema en el mismo cambio.

Migrar por áreas siguiendo el plan. Retirar la implementación anterior solo después de comprobar paridad de ruta, verbo, autorización, rate limit, cuerpos, status, errores, transacción y comportamiento de frontend, OIDC y WhatsApp. No mapear la misma ruta dos veces. Las 41 combinaciones explícitas actuales, incluidas las cuatro condicionales de WhatsApp, son el inventario de partida; OpenIddict y Aspire conservan sus endpoints técnicos.

Las pruebas actuales de arquitectura protegen las referencias entre proyectos y capas. Ampliarlas durante la migración para exigir controllers → servicios y ausencia final del pipeline viejo, sin introducir una regla global que falle solo porque aún existe código en transición. Antes de cerrar la migración, exigir build, tests y verificación de contratos completos según el plan.

Cambiar esta estructura requiere una nueva decisión de arquitectura explícita y documentada; agregar una funcionalidad o mover un archivo no cambia la regla.
