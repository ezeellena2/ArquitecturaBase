# Infrastructure: adaptadores técnicos

Implementa los puertos de Application. `Persistence` encapsula PostgreSQL; `Identity` y `Identity/OpenIddict`, cuentas y protocolo; `Caching`, Redis; `Emails`, entrega; `Phones`, interpretación; `Security`, criptografía; `Settings`, opciones iniciales del seed.

## Al modificar

- Las consultas EF de negocio van detrás de repositorios o lectores. El contexto directo queda para los componentes técnicos autorizados.
- El registro pertenece al adaptador dueño. Persistencia del núcleo se registra solo en `PersistenceRegistration`; los módulos tienen su propio registro.
- Las excepciones expresan fallas de infraestructura. Una falla de negocio usa los errores y resultados de Domain.
- Un singleton no captura servicios scoped. Para caché, workers y tareas de fondo, resolvé el scope de cada operación.
- Verificá comportamiento técnico con integración real; los tests de arquitectura comprueban las fronteras.

## Referencias

- [Reglas de ubicación y acceso](../../docs/architecture/backend.md).
- [Tests técnicos e integración](../../tests/ArquitecturaBase.Api.IntegrationTests).
