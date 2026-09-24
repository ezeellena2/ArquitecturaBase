# ArquitecturaBase: reglas para agentes

La arquitectura canónica del backend está en [`docs/specs/2026-09-24-backend-mvc-architecture.md`](docs/specs/2026-09-24-backend-mvc-architecture.md). El historial y las puertas de verificación de su implementación están en [`docs/plans/2026-09-23-migracion-mvc-servicios-repositorios.md`](docs/plans/2026-09-23-migracion-mvc-servicios-repositorios.md). `CLAUDE.md` conserva las reglas operativas y funcionales del proyecto. Si una descripción histórica de carpetas o del pipeline de handlers contradice la arquitectura canónica, prevalece la especificación nueva.

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

## Contratos y protección de la arquitectura

Las rutas HTTP de negocio se implementan con controllers MVC. No agregar Minimal APIs de negocio ni reintroducir `IEndpoint`, `ICommandHandler`, `IQueryHandler`, `Application/Features`, sus decoradores o Scrutor para casos de uso. OpenIddict y Aspire conservan sus endpoints técnicos.

El inventario de la migración fija 41 combinaciones explícitas de verbo/ruta, incluidas cuatro condicionales de WhatsApp. Una ruta nueva no cambia la arquitectura: conservar verbo, autorización, rate limit, cuerpos, status, errores y transacciones que correspondan, y ampliar el inventario y sus pruebas. No mapear una combinación dos veces. Las pruebas de arquitectura deben exigir controllers → servicios, límites entre capas y ausencia del pipeline anterior. Antes de integrar cambios de negocio, ejecutar build y pruebas pertinentes; para el cierre de la migración, cumplir las puertas automatizadas y manuales del plan.

Cambiar esta estructura requiere una nueva decisión de arquitectura explícita y documentada; agregar una funcionalidad o mover un archivo no cambia la regla.
