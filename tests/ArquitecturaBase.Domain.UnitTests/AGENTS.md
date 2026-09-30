# Domain.UnitTests: reglas puras

Espeja las áreas de Domain y prueba invariantes, entidades, valores y resultados sin proveedores externos.

## Al modificar

- Probá casos válidos, inválidos y límites. Una regla nueva se escribe primero como test que falla.
- No agregar EF, ASP.NET, Redis ni base. Si el escenario necesita esos componentes, pertenece a integración.
- Usá entradas y fechas explícitas; conservá los tests del módulo dentro de `Modules/<Módulo>`.

## Referencias

- [Guía del modelo](../../src/ArquitecturaBase.Domain/AGENTS.md).
- [Responsabilidad de los tests](../../docs/architecture/backend.md).
