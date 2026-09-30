# Application: casos de uso

Coordina operaciones de negocio sin depender de EF ni de HTTP. `Interfaces` contiene puertos; `Services`, casos de uso y helpers; `Models`, datos intercambiados; `Validation`, validadores; `Resources`, textos; `Configuration`, opciones funcionales; `Channels`, adaptadores del núcleo.

## Al modificar

- Las interfaces públicas de casos de uso viven en `Interfaces/Services`; los consumidores externos van en `Interfaces/Integrations` y la persistencia en `Interfaces/Persistence`.
- Un método público que escribe abre un único límite con `IUnitOfWork`; sus helpers no guardan ni abren transacciones. Elegí la política de commit por los efectos que deben persistir.
- Una dependencia técnica se expresa como puerto: sin `DbContext`, `HttpContext`, tipos de Identity, Redis ni `IQueryable`.
- Registrá servicios del núcleo en `DependencyInjection.cs`. Cada módulo registra los suyos dentro de su carpeta; el núcleo no referencia implementaciones opcionales.

## Referencias

- [Casos de uso y límites de guardado](../../docs/architecture/backend.md).
- [Receta de un área nueva](../../docs/guides/agregar-un-area.md).
- [Tests de servicios y validadores](../../tests/ArquitecturaBase.Application.UnitTests).
