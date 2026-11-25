# Tests: qué verifica cada proyecto

Domain.UnitTests prueba reglas puras; Application.UnitTests, validación y coordinación; ArchitectureTests, fronteras y convenciones; Api.IntegrationTests, HTTP y adaptadores reales.

## Al modificar

- Elegí el nivel que observe el comportamiento: no duplicar la implementación en un test ni relajar una regla de arquitectura para hacer pasar código.
- TDD cuando hay lógica. Los nombres describen el comportamiento en inglés y los relojes controlados usan `FakeTimeProvider`.
- Se usa xUnit v3 con Microsoft Testing Platform: para un proyecto, `dotnet test --project <ruta>.csproj`; para una clase agregá `-- --filter-class "<Namespace.Clase>"`.
- Domain, Application y Architecture corren sin servicios externos. La integración requiere Docker con PostgreSQL y Redis; compilarla no equivale a ejecutarla.
- Al terminar una tarea: build sin advertencias, los proyectos unitarios y `ArchitectureTests` enteros y la integración de lo que cambió. La suite completa va una vez por día, o antes de publicar, y cierra todo lo que entró. Si falla, se corrige, se repite solo lo que falló y lo confirma la corrida completa siguiente; las intermedias no se registran. Un test que falla solo junto con los demás tiene datos mal aislados. Si levantaste Aspire, apagalo al terminar.

## Referencias

- [Arnés y cobertura](../docs/architecture/backend.md).
- [Comandos y criterio de cierre](../AGENTS.md).
