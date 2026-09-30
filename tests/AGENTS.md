# Tests: qué verifica cada proyecto

Domain.UnitTests prueba reglas puras; Application.UnitTests, validación y coordinación; ArchitectureTests, fronteras y convenciones; Api.IntegrationTests, HTTP y adaptadores reales.

## Al modificar

- Elegí el nivel que observe el comportamiento: no duplicar la implementación en un test ni relajar una regla de arquitectura para hacer pasar código.
- TDD cuando hay lógica. Los nombres describen el comportamiento en inglés y los relojes controlados usan `FakeTimeProvider`.
- Se usa xUnit v3 con Microsoft Testing Platform: para un proyecto, `dotnet test --project <ruta>.csproj`; para una clase agregá `-- --filter-class "<Namespace.Clase>"`.
- Domain, Application y Architecture corren sin servicios externos. La integración requiere Docker con PostgreSQL y Redis; compilarla no equivale a ejecutarla.
- El cierre requiere build sin advertencias y suite completa. Si levantaste Aspire, apagalo al terminar.

## Referencias

- [Arnés y cobertura](../docs/architecture/backend.md).
- [Comandos y criterio de cierre](../AGENTS.md).
