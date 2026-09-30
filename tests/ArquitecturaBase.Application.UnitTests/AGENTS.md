# Application.UnitTests: coordinación y validación

Espeja servicios, modelos, validadores y recursos de Application. Usa dobles de los puertos para observar decisiones y efectos.

## Al modificar

- No agregar proveedores externos. Probá resultados y efectos relevantes: política de commit, intentos, consumo, invalidación y envío.
- Un doble del Unit of Work debe conservar el significado de `CommitPolicy`, sin simular una base real.
- Usá `FakeTimeProvider` para vencimientos y bloqueos; no demoras reales.
- Un texto o error nuevo requiere paridad y traducción de su código. Un módulo opcional mantiene sus tests en su carpeta.

## Referencias

- [Guía de casos de uso](../../src/ArquitecturaBase.Application/AGENTS.md).
- [Arnés unitario](../../docs/architecture/backend.md).
- [Servicios de referencia](Services/Roles).
